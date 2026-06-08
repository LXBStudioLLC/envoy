using System.Text.Json;

namespace Envoy.Core.Services;

internal class CdpBrowserQueryAdapter : IBrowserQuery
{
    private readonly CdpBrowserService _cdp;

    public CdpBrowserQueryAdapter(CdpBrowserService cdp)
    {
        _cdp = cdp;
    }

    public Task<string?> QuerySelectorAsync(string selector, CancellationToken ct = default)
    {
        return _cdp.QuerySelectorAsync(selector, ct);
    }

    public async Task<string> EvaluateJsAsync(string expression, CancellationToken ct = default)
    {
        var result = await _cdp.SendCommandAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = true
        }, ct);

        if (result.TryGetProperty("result", out var res) && res.TryGetProperty("value", out var val))
        {
            if (val.ValueKind == JsonValueKind.String)
                return val.GetString() ?? "";

            return val.GetRawText();
        }

        return "";
    }
}