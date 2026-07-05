using Microsoft.Extensions.Logging;
using OllamaSharp;
using System.Text;
using System.Text.Json;

namespace Envoy.Core.Services;

public class OllamaProvider : ILLMProvider
{
    public string ProviderId => "ollama";
    public string DisplayName => "Ollama";
    public string Description => "Local LLM inference via Ollama (default)";
    public bool RequiresApiKey => false;
    public bool IsLocal => true;

    // Static HttpClient prevents socket exhaustion. Per-call timeouts via CancellationToken.
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly string _endpoint;
    private readonly string _defaultModel;
    private readonly ILogger<OllamaProvider> _log;
    private readonly OllamaApiClient _apiClient;

    public OllamaProvider(string endpoint, string defaultModel, ILogger<OllamaProvider> log)
    {
        _endpoint = endpoint;
        _defaultModel = defaultModel;
        _log = log;
        _apiClient = new OllamaApiClient(new Uri(endpoint));
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var response = await Http.GetAsync($"{_endpoint}/api/tags", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<LLMModelInfo>> ListModelsAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var response = await Http.GetAsync($"{_endpoint}/api/tags", cts.Token);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cts.Token);

            var doc = JsonDocument.Parse(json);
            var models = new List<LLMModelInfo>();

            if (doc.RootElement.TryGetProperty("models", out var modelsArray))
            {
                foreach (var m in modelsArray.EnumerateArray())
                {
                    var model = new LLMModelInfo
                    {
                        Id = m.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "",
                        Provider = ProviderId,
                        IsLoaded = true
                    };

                    if (m.TryGetProperty("size", out var sizeEl))
                        model.SizeBytes = sizeEl.GetInt64();

                    if (m.TryGetProperty("details", out var details))
                    {
                        if (details.TryGetProperty("family", out var fam))
                            model.Family = fam.GetString();
                        if (details.TryGetProperty("quantization_level", out var quant))
                            model.Quantization = quant.GetString();
                        if (details.TryGetProperty("parent_model", out var parent))
                            model.Name = parent.GetString() ?? model.Id;
                    }

                    model.Name = string.IsNullOrEmpty(model.Name) ? model.Id : model.Name;
                    models.Add(model);
                }
            }

            return models;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to list Ollama models from {Endpoint}", _endpoint);
            return new List<LLMModelInfo>();
        }
    }

    public async Task<string> CompleteAsync(string prompt, string systemPrompt, string? modelId, CancellationToken ct = default)
    {
        var model = modelId ?? _defaultModel;
        // Reuse one OllamaApiClient (built in the ctor) instead of creating and
        // disposing a fresh client, with its own HttpClient, on every call.
        _apiClient.SelectedModel = model;

        try
        {
            var chat = string.IsNullOrWhiteSpace(systemPrompt)
                ? new Chat(_apiClient)
                : new Chat(_apiClient, systemPrompt);

            var sb = new StringBuilder();
            await foreach (var token in chat.SendAsync(prompt, ct))
                sb.Append(token);

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ollama completion failed with model {Model}", model);
            throw;
        }
    }

    public async Task<T?> CompleteJsonAsync<T>(string prompt, string systemPrompt, string? modelId, CancellationToken ct = default)
    {
        var text = await CompleteAsync(prompt, systemPrompt, modelId, ct);
        text = OllamaService.ExtractJson(text);
        try
        {
            return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "Ollama JSON deserialization failed for {Type}", typeof(T).Name);
            return default;
        }
    }
}
