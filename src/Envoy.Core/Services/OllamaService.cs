using Microsoft.Extensions.Logging;
using OllamaSharp;
using System.Text;
using System.Text.Json;

namespace Envoy.Core.Services;

public class OllamaService : IDisposable
{
    private ILLMProvider _activeProvider;
    private readonly OllamaApiClient? _chatClient;
    private readonly string _modelName;
    private readonly ILogger<OllamaService> _log;
    private bool _disposed;
    private readonly object _providerLock = new();

    [Obsolete("Use LLMDetectionService.CreateActiveProvider() instead. This constructor is kept for backward compatibility.")]
    public OllamaService(string modelName = "qwen2.5-coder:14b", string endpoint = "http://localhost:11434", ILogger<OllamaService>? log = null)
    {
        _modelName = modelName;
        _log = log ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<OllamaService>.Instance;
        _chatClient = new OllamaApiClient(new Uri(endpoint)) { SelectedModel = modelName };
        _activeProvider = null!;
        _log.LogInformation("OllamaService (legacy) initialized with model {Model} at {Endpoint}", modelName, endpoint);
    }

    public OllamaService(ILLMProvider provider, ILogger<OllamaService>? log = null)
    {
        _activeProvider = provider;
        _modelName = provider.DisplayName;
        _log = log ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<OllamaService>.Instance;
        _chatClient = null;
        _log.LogInformation("OllamaService initialized with provider {Provider} ({ProviderId})", provider.DisplayName, provider.ProviderId);
    }

    public void SwitchProvider(ILLMProvider newProvider)
    {
        lock (_providerLock)
        {
            _activeProvider = newProvider;
            _log.LogInformation("Switched LLM provider to {Provider} ({ProviderId})", newProvider.DisplayName, newProvider.ProviderId);
        }
    }

    public async Task<string> CompleteAsync(string prompt, string systemPrompt = "", CancellationToken ct = default)
    {
        var provider = _activeProvider;
        if (provider != null)
        {
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

        try
        {
            var chat = string.IsNullOrWhiteSpace(systemPrompt)
                ? new Chat(_chatClient!)
                : new Chat(_chatClient!, systemPrompt);

            var sb = new StringBuilder();
            await foreach (var token in chat.SendAsync(prompt, ct))
                sb.Append(token);

            var text = sb.ToString();
            _log.LogDebug("LLM response: {Length} chars", text.Length);
            return text;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LLM completion failed for model {Model}", _modelName);
            throw;
        }
    }

    public async Task<T?> CompleteJsonAsync<T>(string prompt, string systemPrompt = "", CancellationToken ct = default)
    {
        var provider = _activeProvider;
        if (provider != null)
        {
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

        var text = await CompleteAsync(prompt, systemPrompt, ct);
        text = ExtractJson(text);
        try
        {
            return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "Failed to deserialize LLM JSON response for {Type}. Raw: {Raw}", typeof(T).Name, text[..Math.Min(text.Length, 200)]);
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

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _chatClient?.Dispose();
        }
    }
}
