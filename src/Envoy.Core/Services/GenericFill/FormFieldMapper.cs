using Envoy.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Envoy.Core.Services;

/// <summary>
/// Maps enumerated form fields to (action, value-source) using the LLM
/// (APP-09). The plan's values are constrained to references:
/// "<c>pf:</c>{key}" for profile-catalog keys and "<c>#</c>{id}" for
/// answer-bank candidate ids — free-form invented strings in the plan are
/// rejected at validation, which is what turns "an LLM fills your form" from
/// a trust problem into a structurally-bounded one.
/// </summary>
public sealed class FormFieldMapper
{
    /// <summary>Maximum fields sent in ONE mapping call (keeps the prompt bounded).</summary>
    public const int MaxFieldsPerCall = 80;

    private readonly OllamaService _llm;
    private readonly IAnswerBankRepository _bank;
    private readonly ILogger<FormFieldMapper>? _log;

    public FormFieldMapper(OllamaService llm, IAnswerBankRepository bank, ILogger<FormFieldMapper>? log = null)
    {
        _llm = llm;
        _bank = bank;
        _log = log;
    }

    /// <summary>Validate + normalize a raw plan coming from the model.</summary>
    public static List<FieldPlan> Validate(
        IReadOnlyList<FormFieldInfo> fields,
        IReadOnlyList<RawPlanItem> rawPlans,
        int bankCount)
    {
        var plans = new List<FieldPlan>();
        var seen = new HashSet<int>();

        foreach (var raw in rawPlans)
        {
            if (raw.Index < 0 || raw.Index >= fields.Count)
                continue; // out-of-range index: ignore, the field itself gets a needs-human default below

            if (!seen.Add(raw.Index))
                continue; // duplicate plan for one field: first-wins

            var field = fields[raw.Index];
            var action = ParseAction(raw.Action);
            var valueSource = raw.ValueSource?.Trim();

            if (action is FieldPlanAction.Skip or FieldPlanAction.NeedsHuman)
            {
                plans.Add(new FieldPlan(raw.Index, action, null, raw.Note));
                continue;
            }

            // Action must match the widget type.
            var typeOk = action switch
            {
                FieldPlanAction.Fill => FormFieldInfo.FillableTypes.Contains(field.Type),
                FieldPlanAction.Select => field.Type == "select",
                FieldPlanAction.Toggle => field.Type is "checkbox" or "radio",
                FieldPlanAction.Upload => field.Type == "file",
                _ => false
            };

            if (!typeOk)
            {
                plans.Add(new FieldPlan(raw.Index, FieldPlanAction.NeedsHuman, null, "action/type mismatch"));
                continue;
            }

            // ── Per-action value-source checks (each action decides its own grammar) ──

            if (action == FieldPlanAction.Select)
            {
                var matched = valueSource is not null &&
                    field.Options.Contains(valueSource, StringComparer.OrdinalIgnoreCase);
                if (!matched)
                {
                    plans.Add(new FieldPlan(raw.Index, FieldPlanAction.NeedsHuman, null, "no exactly matching option"));
                    continue;
                }
                plans.Add(new FieldPlan(raw.Index, action, valueSource, raw.Note));
                continue;
            }

            if (action == FieldPlanAction.Toggle)
            {
                var isBool = valueSource is "pf:true" or "pf:false";
                if (!isBool)
                {
                    plans.Add(new FieldPlan(raw.Index, FieldPlanAction.NeedsHuman, null, "toggle must be pf:true / pf:false"));
                    continue;
                }
                plans.Add(new FieldPlan(raw.Index, action, valueSource, raw.Note));
                continue;
            }

            if (string.IsNullOrWhiteSpace(valueSource))
            {
                plans.Add(new FieldPlan(raw.Index, FieldPlanAction.NeedsHuman, null, "empty value"));
                continue;
            }

            if (valueSource.StartsWith("pf:", StringComparison.OrdinalIgnoreCase))
            {
                var key = valueSource[3..];
                if (!FormProfileCatalog.IsKnownKey(key))
                {
                    plans.Add(new FieldPlan(raw.Index, FieldPlanAction.NeedsHuman, null, "unknown profile key"));
                    continue;
                }
                plans.Add(new FieldPlan(raw.Index, action, valueSource, raw.Note));
                continue;
            }

            if (valueSource.StartsWith("#", StringComparison.Ordinal) &&
                int.TryParse(valueSource[1..], out var bankId) &&
                bankId >= 0 && bankId < bankCount)
            {
                plans.Add(new FieldPlan(raw.Index, action, "#" + bankId, raw.Note));
                continue;
            }

            // Anything else — free text, invented profile keys, phantom bank
            // ids — is an invention attempt: refuse to a human hand.
            plans.Add(new FieldPlan(raw.Index, FieldPlanAction.NeedsHuman, null, "invented value refused"));
        }

        // Fields with NO plan default to needs-human (never silently left).
        foreach (var field in fields.Take(MaxFieldsPerCall))
        {
            if (!seen.Contains(field.Index))
                plans.Add(new FieldPlan(field.Index, FieldPlanAction.NeedsHuman, null, "unmapped"));
        }

        return plans;
    }

    private static FieldPlanAction ParseAction(string? action) => action?.Trim().ToLowerInvariant() switch
    {
        "fill" => FieldPlanAction.Fill,
        "select" => FieldPlanAction.Select,
        "toggle" => FieldPlanAction.Toggle,
        "upload" => FieldPlanAction.Upload,
        "skip" => FieldPlanAction.Skip,
        "needs_human" => FieldPlanAction.NeedsHuman,
        _ => FieldPlanAction.NeedsHuman
    };

    /// <summary>Real implementation: prompt + parse + validate.</summary>
    public async Task<(List<FieldPlan> Plans, string? Error)> MapAsync(
        IReadOnlyList<FormFieldInfo> fields,
        MasterProfile profile,
        CancellationToken ct = default)
    {
        var boundedFields = fields.Take(MaxFieldsPerCall).ToList();
        if (boundedFields.Count == 0)
            return (new List<FieldPlan>(), null);

        var bankEntries = await _bank.GetAllAsync(ct);

        // Match live question labels to the bank BEFORE the model: the only
        // bank candidates offered are ones the fuzzy matcher already trusts.
        var candidates = new List<(int Slot, int BankId, AnswerBankEntry Entry, string LiveLabel)>();
        foreach (var field in boundedFields)
        {
            if (!FormFieldPrompts.QuestionLikely(field.Label, field.Type))
                continue;

            if (AnswerBankMatcher.FindBest(bankEntries, field.Label, out _) is { } hit)
                candidates.Add((candidates.Count, bankEntries.IndexOf(hit), hit, field.Label));
        }

        var (prompt, system) = FormFieldPrompts.BuildPrompt(boundedFields, profile, candidates);

        try
        {
            var response = await _llm.CompleteJsonAsync<MapperPayload>(prompt, system, ct: ct);
            if (response?.Plans is not { Count: > 0 })
            {
                _log?.LogWarning("Field mapping returned no plan; fields fall back to needs-human");
                return (Validate(boundedFields, new List<RawPlanItem>(), candidates.Count), null);
            }

            return (Validate(boundedFields, response.Plans, candidates.Count), null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Field mapping failed");
            return (new List<FieldPlan>(), ex.Message);
        }
    }

    /// <summary>What the model must produce; permissive on the wire, strict at validation.</summary>
    public sealed class MapperPayload
    {
        [JsonPropertyName("fields")]
        public List<RawPlanItem> Plans { get; set; } = new();
    }

    public sealed record RawPlanItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("action")] string? Action,
        [property: JsonPropertyName("value_source")] string? ValueSource,
        [property: JsonPropertyName("note")] string? Note);
}
