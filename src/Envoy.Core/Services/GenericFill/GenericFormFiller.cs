using Envoy.Core.Models;
using Microsoft.Extensions.Logging;

namespace Envoy.Core.Services;

/// <summary>
/// The generic auto-fill pipeline (APP-09): enumerate → map → actuate. The
/// orchestrator falls back to this when no hand template matches the page, so
/// any mainstream ATS becomes fillable; per-field results are honest and
/// unmapped/unsafe fields surface for human hands (never invented).
/// </summary>
public sealed class GenericFormFiller
{
    private readonly IFormFieldEnumerator _enumerator;
    private readonly IFormFieldActuator _actuator;
    private readonly FormFieldMapper _mapper;
    private readonly ILogger<GenericFormFiller>? _log;
    private IReadOnlyList<FormFieldMapper.SlotCandidate> _slots = Array.Empty<FormFieldMapper.SlotCandidate>();

    public GenericFormFiller(IFormFieldEnumerator enumerator, IFormFieldActuator actuator,
        FormFieldMapper mapper, ILogger<GenericFormFiller>? log = null)
    {
        _enumerator = enumerator;
        _actuator = actuator;
        _mapper = mapper;
        _log = log;
    }

    /// <summary>
    /// Fill what can be filled. The resume upload is handled here (when the
    /// tailored PDF exists at the shared sanitized path); everything else comes
    /// from the mapped plan. Returns an honest result summary for the
    /// confirmation gate.
    /// </summary>
    public async Task<GenericFillResult> FillAsync(
        TailoredProfile tailored,
        MasterProfile profile,
        CancellationToken ct = default)
    {
        var fields = await _enumerator.EnumerateAsync(ct);
        if (fields.Count == 0)
        {
            return new GenericFillResult { FieldCount = 0, FilledCount = 0, Notes = new[] { "No fillable form fields were found on this page." } };
        }

        var (plans, slots, error) = await _mapper.MapAsync(fields, profile, ct);
        if (error is not null)
        {
            _log?.LogWarning("Generic fill aborted: mapping failed ({Error})", error);
            return new GenericFillResult
            {
                FieldCount = fields.Count,
                Notes = new[] { $"Mapping failed: {error}" }
            };
        }

        var plansByIndex = plans.ToDictionary(p => p.Index);
        var filled = 0;
        var needsHuman = new List<string>();
        var notes = new List<string>();

        foreach (var field in fields)
        {
            ct.ThrowIfCancellationRequested();

            var plan = plansByIndex.TryGetValue(field.Index, out var p)
                ? p
                : new FieldPlan(field.Index, FieldPlanAction.NeedsHuman, null, "unmapped");

            switch (plan.Action)
            {
                case FieldPlanAction.Fill:
                    {
                        string? value;
                        if (plan.ValueSource is not null && plan.ValueSource.StartsWith("#", StringComparison.Ordinal)
                            && int.TryParse(plan.ValueSource[1..], out var fillSlot)
                            && fillSlot < slots.Count)
                            value = slots[fillSlot].Text; // stored answer text, pre-matched to the question label
                        else
                            value = FormProfileCatalog.Resolve(ExtractSource(plan.ValueSource), profile);

                        if (value is not null)
                        {
                            if (await _actuator.FillTextAsync(field.Selector, value, ct))
                                filled++;
                            else
                            {
                                needsHuman.Add(field.Label + " (element not found)");
                                notes.Add($"fill failed: {field.Label}");
                            }
                        }
                        else
                        {
                            needsHuman.Add(field.Label + $" (profile has no '{ExtractSource(plan.ValueSource)}' value)");
                        }
                        break;
                    }

                case FieldPlanAction.Select:
                    {
                        var option = plan.ValueSource?.Trim();
                        if (plan.ValueSource is null || option is null)
                        {
                            needsHuman.Add(field.Label);
                            break;
                        }
                        // For select plans the value source IS the option text
                        // (validated against options earlier).
                        if (await _actuator.SelectOptionAsync(field.Selector, option, ct))
                            filled++;
                        else
                        {
                            needsHuman.Add(field.Label + " (option missing at execution time)");
                        }
                        break;
                    }

                case FieldPlanAction.Toggle:
                    {
                        // pf:true / pf:false directly, or a bank slot whose
                        // stored answer pre-normalized to a truth value at
                        // candidate build. Prose answers were never slotted,
                        // so prose never clicks.
                        bool? want;
                        if (plan.ValueSource?.ToLowerInvariant() is "pf:true" or "pf:false")
                        {
                            want = plan.ValueSource.EndsWith("true", StringComparison.OrdinalIgnoreCase);
                        }
                        else if (plan.ValueSource is not null
                            && plan.ValueSource.StartsWith("#", StringComparison.Ordinal)
                            && int.TryParse(plan.ValueSource[1..], out var toggleSlot)
                            && toggleSlot < slots.Count)
                        {
                            want = slots[toggleSlot].Truth;
                        }
                        else
                        {
                            want = null;
                        }

                        if (want is null)
                        {
                            needsHuman.Add(field.Label + " (stored answer does not read as yes/no)");
                            notes.Add($"toggle unresolved: {field.Label}");
                            break;
                        }

                        if (await _actuator.SetCheckedAsync(field.Selector, want.Value, ct))
                            filled++;
                        else
                            needsHuman.Add(field.Label + " (control not found)");
                        break;
                    }

                case FieldPlanAction.Upload:
                    {
                        var pdfPath = ResumeFilePath.For(tailored.TailoredData.Name, tailored.Company, tailored.JobTitle);
                        if (File.Exists(pdfPath) && await _actuator.UploadFileAsync(field.Selector, pdfPath, ct))
                            filled++;
                        else
                            needsHuman.Add(field.Label + " (resume file not found or upload rejected)");
                        break;
                    }

                case FieldPlanAction.Skip:
                    break;

                case FieldPlanAction.NeedsHuman:
                default:
                    needsHuman.Add(field.Label + (plan.Note is null ? "" : $" ({plan.Note})"));
                    break;
            }
        }

        _log?.LogInformation(
            "Generic fill: {Count} fields enumerated, {Filled} filled, {Human} need a human",
            fields.Count, filled, needsHuman.Count);

        return new GenericFillResult
        {
            FieldCount = fields.Count,
            FilledCount = filled,
            NeedsHuman = needsHuman,
            Notes = notes
        };
    }

    private static string ExtractSource(string? valueSource) =>
        valueSource?.StartsWith("pf:", StringComparison.OrdinalIgnoreCase) == true
            ? valueSource[3..]
            : valueSource ?? string.Empty;

    /// <summary>
    /// Normalizes a stored bank answer ("Yes", "true", "1", "I agree" style
    /// affirmatives) into a checkbox truth value. Anything that does not read
    /// as an unambiguous yes/no returns null — prose never becomes a click.
    /// </summary>
    /// <summary>
    /// Normalizes a stored bank answer ("Yes", "true", "1", "I agree" style
    /// affirmatives) into a checkbox truth value. Anything that does not read
    /// as an unambiguous yes/no returns null — prose never becomes a click.
    /// </summary>
}
