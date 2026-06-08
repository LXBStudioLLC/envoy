using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Envoy.Core.Services;

public class OllamaService
{
    // Reference assignment to _activeProvider is atomic on .NET. CompleteAsync
    // snapshots the reference into a local before using it, so a concurrent
    // SwitchProvider can't NRE the in-flight call — the worst case is an
    // already-running completion finishes against the prior provider, which
    // is the desired semantics anyway. No lock needed.
    private ILLMProvider _activeProvider;
    private readonly ILogger<OllamaService> _log;

    public OllamaService(ILLMProvider provider, ILogger<OllamaService>? log = null)
    {
        _activeProvider = provider;
        _log = log ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<OllamaService>.Instance;
        _log.LogInformation("OllamaService initialized with provider {Provider} ({ProviderId})", provider.DisplayName, provider.ProviderId);
    }

    public void SwitchProvider(ILLMProvider newProvider)
    {
        _activeProvider = newProvider;
        _log.LogInformation("Switched LLM provider to {Provider} ({ProviderId})", newProvider.DisplayName, newProvider.ProviderId);
    }

    public async Task<string> CompleteAsync(string prompt, string systemPrompt = "", CancellationToken ct = default)
    {
        var provider = _activeProvider;
        try
        {
            return await provider.CompleteAsync(prompt, systemPrompt, null, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LLM completion failed via {Provider}", provider.DisplayName);
            throw;
        }
    }

    public async Task<T?> CompleteJsonAsync<T>(string prompt, string systemPrompt = "", CancellationToken ct = default)
    {
        var provider = _activeProvider;
        try
        {
            return await provider.CompleteJsonAsync<T>(prompt, systemPrompt, null, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LLM JSON completion failed via {Provider}", provider.DisplayName);
            return default;
        }
    }

    public static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0) return text;

        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escape = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString) continue;

            if (c == '{') depth++;
            else if (c == '}') depth--;

            if (depth == 0)
                return text[start..(i + 1)];
        }

        return text[start..];
    }
}
