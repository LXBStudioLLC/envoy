namespace Envoy.Core.Services;

public interface IElementLocator
{
    Task<LocateResult> LocateAsync(TemplateStep step, string templateId, CancellationToken ct = default);
}

public sealed record LocateResult(
    string? NodeId,
    string? ResolvedSelector,
    double Confidence,
    bool DidRelocate,
    string? OriginalSelector,
    string? FailureReason);