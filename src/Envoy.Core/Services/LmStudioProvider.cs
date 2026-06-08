using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Envoy.Core.Services;

public class LmStudioProvider : ILLMProvider
{
    public string ProviderId => "lmstudio";
    public string DisplayName => "LM Studio";
    public string Description => "Local LLM inference via LM Studio (OpenAI-compatible)";
    public bool RequiresApiKey => false;
    public bool IsLocal => true;

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly string _endpoint;
    private readonly ILogger<LmStudioProvider> _log;

    public LmStudioProvider(string endpoint, ILogger<LmStudioProvider> log)
    {
        _endpoint = endpoint.TrimEnd('/');
        _log = log;
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var response = await Http.GetAsync($"{_endpoint}/v1/models", cts.Token);
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
            var response = await Http.GetAsync($"{_endpoint}/v1/models", cts.Token);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cts.Token);

            var doc = JsonDocument.Parse(json);
            var models = new List<LLMModelInfo>();

            if (doc.RootElement.TryGetProperty("data", out var dataArray))
            {
                foreach (var m in dataArray.EnumerateArray())
                {
                    var model = new LLMModelInfo
                    {
                        Id = m.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "",
                        Provider = ProviderId,
                        IsLoaded = true
                    };

                    model.Name = model.Id;
                    models.Add(model);
                }
            }

            return models;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to list LM Studio models from {Endpoint}", _endpoint);
            return new List<LLMModelInfo>();
        }
    }

    public async Task<string> CompleteAsync(string prompt, string systemPrompt, string? modelId, CancellationToken ct = default)
    {
        var model = modelId ?? await ChooseDefaultModelAsync();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(5));

        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = string.IsNullOrWhiteSpace(systemPrompt)
                ? new[] { new { role = "user", content = prompt } }
                : new object[] { new { role = "system", content = systemPrompt }, new { role = "user", content = prompt } },
            ["temperature"] = 0.3,
            ["stream"] = false
        };

        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await Http.PostAsync($"{_endpoint}/v1/chat/completions", content, cts.Token);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cts.Token);
        var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var msgContent))
            {
                return msgContent.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
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
            _log.LogError(ex, "LM Studio JSON deserialization failed for {Type}", typeof(T).Name);
            return default;
        }
    }

    private async Task<string> ChooseDefaultModelAsync()
    {
        var models = await ListModelsAsync();
        return models.FirstOrDefault()?.Id ?? "default";
    }
}