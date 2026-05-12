using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Envoy.Core.Services;

public class CloudLLMProvider : ILLMProvider
{
    public string ProviderId { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public bool RequiresApiKey => true;
    public bool IsLocal => false;

    // Static HttpClient prevents socket exhaustion across the cloud providers.
    // Per-call timeouts go through CancellationToken; auth goes on the request
    // message (not DefaultRequestHeaders) so we can share one client safely.
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly string _apiKey;
    private readonly string _endpoint;
    private readonly string _defaultModel;
    private readonly ILogger<CloudLLMProvider> _log;

    private CloudLLMProvider(string providerId, string displayName, string description, string apiKey, string endpoint, string defaultModel, ILogger<CloudLLMProvider> log)
    {
        ProviderId = providerId;
        DisplayName = displayName;
        Description = description;
        _apiKey = apiKey;
        _endpoint = endpoint.TrimEnd('/');
        _defaultModel = defaultModel;
        _log = log;
    }

    public static CloudLLMProvider OpenAI(string apiKey, ILogger<CloudLLMProvider> log)
        => new("openai", "OpenAI", "GPT-4o, GPT-4o-mini via OpenAI API", apiKey, "https://api.openai.com", "gpt-4o-mini", log);

    public static CloudLLMProvider Anthropic(string apiKey, ILogger<CloudLLMProvider> log)
        => new("anthropic", "Anthropic", "Claude 3.5 Sonnet, Haiku via Anthropic API", apiKey, "https://api.anthropic.com", "claude-3-5-sonnet-20241022", log);

    public static CloudLLMProvider Gemini(string apiKey, ILogger<CloudLLMProvider> log)
        => new("gemini", "Google Gemini", "Gemini 1.5 Pro, Flash via Google AI API", apiKey, "https://generativelanguage.googleapis.com", "gemini-1.5-flash", log);

    private void ApplyAuth(HttpRequestMessage req)
    {
        switch (ProviderId)
        {
            case "openai":
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                break;
            case "anthropic":
                req.Headers.Add("x-api-key", _apiKey);
                req.Headers.Add("anthropic-version", "2023-06-01");
                break;
            case "gemini":
                req.Headers.Add("x-goog-api-key", _apiKey);
                break;
        }
    }

    public async Task<bool> IsAvailableAsync()
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) return false;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            if (ProviderId == "openai")
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
                ApplyAuth(req);
                var resp = await Http.SendAsync(req, cts.Token);
                return resp.IsSuccessStatusCode;
            }
            if (ProviderId == "gemini")
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://generativelanguage.googleapis.com/v1beta/models");
                ApplyAuth(req);
                var resp = await Http.SendAsync(req, cts.Token);
                return resp.IsSuccessStatusCode;
            }
            if (ProviderId == "anthropic")
            {
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<LLMModelInfo>> ListModelsAsync()
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) return new List<LLMModelInfo>();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            if (ProviderId == "openai")
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
                ApplyAuth(req);
                var resp = await Http.SendAsync(req, cts.Token);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync(cts.Token);
                var doc = JsonDocument.Parse(json);

                var models = new List<LLMModelInfo>();
                if (doc.RootElement.TryGetProperty("data", out var data))
                {
                    foreach (var m in data.EnumerateArray())
                    {
                        var id = m.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                        if (id.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) || id.StartsWith("o1-", StringComparison.OrdinalIgnoreCase))
                        {
                            models.Add(new LLMModelInfo
                            {
                                Id = id,
                                Name = id,
                                Provider = ProviderId,
                                IsLoaded = true
                            });
                        }
                    }
                }
                return models;
            }

            if (ProviderId == "gemini")
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://generativelanguage.googleapis.com/v1beta/models");
                ApplyAuth(req);
                var resp = await Http.SendAsync(req, cts.Token);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync(cts.Token);
                var doc = JsonDocument.Parse(json);

                var models = new List<LLMModelInfo>();
                if (doc.RootElement.TryGetProperty("models", out var modelsArr))
                {
                    foreach (var m in modelsArr.EnumerateArray())
                    {
                        var name = m.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                        var displayName = m.TryGetProperty("displayName", out var dispEl) ? dispEl.GetString() ?? name : name;
                        if (name.Contains("generateContent", StringComparison.OrdinalIgnoreCase))
                        {
                            models.Add(new LLMModelInfo
                            {
                                Id = name.Replace("models/", ""),
                                Name = displayName,
                                Provider = ProviderId,
                                IsLoaded = true
                            });
                        }
                    }
                }
                return models;
            }

            if (ProviderId == "anthropic")
            {
                return new List<LLMModelInfo>
                {
                    new() { Id = "claude-sonnet-4-20250514", Name = "Claude Sonnet 4", Provider = ProviderId, IsLoaded = true },
                    new() { Id = "claude-3-5-sonnet-20241022", Name = "Claude 3.5 Sonnet", Provider = ProviderId, IsLoaded = true },
                    new() { Id = "claude-3-5-haiku-20241022", Name = "Claude 3.5 Haiku", Provider = ProviderId, IsLoaded = true }
                };
            }

            return new List<LLMModelInfo>();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to list {Provider} models", DisplayName);
            return new List<LLMModelInfo>();
        }
    }

    public async Task<string> CompleteAsync(string prompt, string systemPrompt, string? modelId, CancellationToken ct = default)
    {
        var model = modelId ?? _defaultModel;

        if (ProviderId == "anthropic")
            return await CompleteAnthropicAsync(prompt, systemPrompt, model, ct);

        return await CompleteOpenAICompatAsync(prompt, systemPrompt, model, ct);
    }

    private async Task<string> CompleteOpenAICompatAsync(string prompt, string systemPrompt, string model, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(5));

        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new { role = "system", content = systemPrompt });
        messages.Add(new { role = "user", content = prompt });

        string url = ProviderId == "gemini"
            ? $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"
            : $"{_endpoint}/v1/chat/completions";

        var body = ProviderId == "gemini"
            ? (object)new
            {
                contents = new[] { new { role = "user", parts = new[] { new { text = (string.IsNullOrWhiteSpace(systemPrompt) ? "" : systemPrompt + "\n\n") + prompt } } } }
            }
            : new Dictionary<string, object>
            {
                ["model"] = model,
                ["messages"] = messages,
                ["temperature"] = 0.3
            };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        ApplyAuth(req);

        var response = await Http.SendAsync(req, cts.Token);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cts.Token);
        var doc = JsonDocument.Parse(responseJson);

        if (ProviderId == "gemini")
        {
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var parts = candidates[0].GetProperty("content").GetProperty("parts");
                if (parts.GetArrayLength() > 0)
                    return parts[0].GetProperty("text").GetString() ?? "";
            }
            return "";
        }

        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var msgContent))
                return msgContent.GetString() ?? "";
        }

        return "";
    }

    private async Task<string> CompleteAnthropicAsync(string prompt, string systemPrompt, string model, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(5));

        var messages = new List<object> { new { role = "user", content = prompt } };

        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["max_tokens"] = 4096,
            ["messages"] = messages
        };

        if (!string.IsNullOrWhiteSpace(systemPrompt))
            body["system"] = systemPrompt;

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        ApplyAuth(req);

        var response = await Http.SendAsync(req, cts.Token);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cts.Token);
        var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.TryGetProperty("content", out var contentArr) && contentArr.GetArrayLength() > 0)
        {
            var firstBlock = contentArr[0];
            if (firstBlock.TryGetProperty("text", out var textEl))
                return textEl.GetString() ?? "";
        }

        return "";
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
            _log.LogError(ex, "{Provider} JSON deserialization failed for {Type}", DisplayName, typeof(T).Name);
            return default;
        }
    }
}
