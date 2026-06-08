namespace Envoy.Core.Services;

public interface IBrowserQuery
{
    Task<string?> QuerySelectorAsync(string selector, CancellationToken ct = default);
    Task<string> EvaluateJsAsync(string expression, CancellationToken ct = default);
}