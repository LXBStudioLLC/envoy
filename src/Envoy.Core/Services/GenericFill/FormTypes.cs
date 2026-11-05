namespace Envoy.Core.Services;

/// <summary>
/// One interactive widget on an application form, enumerated by the DOM scan
/// (APP-09). Selector is the stable handle every later step uses.
/// </summary>
public sealed record FormFieldInfo(
    int Index,
    string Type,
    string Label,
    string Selector,
    bool Required,
    IReadOnlyList<string> Options,
    string CurrentValue)
{
    /// <summary>Text-like fields that the Fill action may target.</summary>
    public static readonly HashSet<string> FillableTypes = new(StringComparer.OrdinalIgnoreCase)
        { "text", "email", "tel", "url", "number", "textarea" };

    /// <summary>Fields the engine refuses outright (passwords, dates it cannot verify, hidden).</summary>
    public static readonly HashSet<string> BannedTypes = new(StringComparer.OrdinalIgnoreCase)
        { "password", "hidden", "date", "datetime-local", "month", "time", "week" };
}

/// <summary>What the mapper wants done with a field.</summary>
public enum FieldPlanAction
{
    Fill,
    Select,
    Toggle,
    Upload,
    Skip,
    NeedsHuman
}

/// <summary>
/// One mapped field. VALUE IS STRUCTURALLY CONSTRAINED: it must be
/// "<c>pf:</c>{key}" (a whitelisted profile key) or "<c>#</c>{id}" (an answer-bank
/// entry id handed to the mapper as a candidate). Free-form invented values in
/// the plan are rejected at validation — the invention ban is enforced by
/// shape, not by trusting the model.
/// </summary>
public sealed record FieldPlan(int Index, FieldPlanAction Action, string? ValueSource, string? Note);

/// <summary>Honest outcome of a generic fill run (APP-05 discipline applies here too).</summary>
public sealed class GenericFillResult
{
    public int FieldCount { get; init; }
    public int FilledCount { get; init; }
    /// <summary>Fields the mapper could not safely answer; the user fills these by hand.</summary>
    public IReadOnlyList<string> NeedsHuman { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}
