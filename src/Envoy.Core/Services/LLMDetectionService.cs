using Envoy.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Envoy.Core.Services;

public class LLMDetectionService
{
    private readonly ILogger<LLMDetectionService> _log;
    private readonly ILoggerFactory _loggerFactory;
    private readonly EnvoySettings _settings;

    public LLMDetectionService(ILogger<LLMDetectionService> log, ILoggerFactory loggerFactory, EnvoySettings settings)
    {
        _log = log;
        _loggerFactory = loggerFactory;
        _settings = settings;
    }

    public async Task<List<LLMConnectionStatus>> DetectAllAsync()
    {
        var results = new List<LLMConnectionStatus>();

        var ollama = new OllamaProvider(
            string.IsNullOrWhiteSpace(_settings.OllamaEndpoint) ? "http://localhost:11434" : _settings.OllamaEndpoint,
            _settings.PreferredModel ?? "qwen2.5-coder:14b",
            _loggerFactory.CreateLogger<OllamaProvider>());

        var lmStudioPort = _settings.LmStudioPort ?? 1234;
        var lmStudio = new LmStudioProvider(
            $"http://localhost:{lmStudioPort}",
            _loggerFactory.CreateLogger<LmStudioProvider>());

        var ollamaTask = CheckProviderAsync(ollama);
        var lmStudioTask = CheckProviderAsync(lmStudio);

        await Task.WhenAll(ollamaTask, lmStudioTask);

        var ollamaStatus = ollamaTask.Result;
        ollamaStatus.Endpoint = _settings.OllamaEndpoint ?? "http://localhost:11434";
        results.Add(ollamaStatus);

        var lmStudioStatus = lmStudioTask.Result;
        lmStudioStatus.Endpoint = $"http://localhost:{lmStudioPort}";
        results.Add(lmStudioStatus);

        if (!string.IsNullOrWhiteSpace(_settings.OpenAIApiKey))
        {
            var openai = CloudLLMProvider.OpenAI(_settings.OpenAIApiKey, _loggerFactory.CreateLogger<CloudLLMProvider>());
            var openaiStatus = await CheckProviderAsync(openai);
            openaiStatus.Endpoint = "https://api.openai.com";
            results.Add(openaiStatus);
        }

        if (!string.IsNullOrWhiteSpace(_settings.AnthropicApiKey))
        {
            var anthropic = CloudLLMProvider.Anthropic(_settings.AnthropicApiKey, _loggerFactory.CreateLogger<CloudLLMProvider>());
            var anthropicStatus = await CheckProviderAsync(anthropic);
            anthropicStatus.Endpoint = "https://api.anthropic.com";
            results.Add(anthropicStatus);
        }

        if (!string.IsNullOrWhiteSpace(_settings.GeminiApiKey))
        {
            var gemini = CloudLLMProvider.Gemini(_settings.GeminiApiKey, _loggerFactory.CreateLogger<CloudLLMProvider>());
            var geminiStatus = await CheckProviderAsync(gemini);
            geminiStatus.Endpoint = "https://generativelanguage.googleapis.com";
            results.Add(geminiStatus);
        }

        return results;
    }

    public ILLMProvider CreateActiveProvider()
    {
        var activeProvider = _settings.ActiveLLMProvider ?? "ollama";
        return CreateProvider(activeProvider);
    }

    public ILLMProvider CreateActiveProvider(string providerId)
    {
        return CreateProvider(providerId);
    }

    private ILLMProvider CreateProvider(string providerId)
    {
        var activeModel = _settings.ActiveLLMModel;

        return providerId switch
        {
            "ollama" => new OllamaProvider(
                string.IsNullOrWhiteSpace(_settings.OllamaEndpoint) ? "http://localhost:11434" : _settings.OllamaEndpoint,
                activeModel ?? _settings.PreferredModel ?? "qwen2.5-coder:14b",
                _loggerFactory.CreateLogger<OllamaProvider>()),
            "lmstudio" => new LmStudioProvider(
                $"http://localhost:{_settings.LmStudioPort ?? 1234}",
                _loggerFactory.CreateLogger<LmStudioProvider>()),
            "openai" => CloudLLMProvider.OpenAI(
                _settings.OpenAIApiKey ?? "",
                _loggerFactory.CreateLogger<CloudLLMProvider>()),
            "anthropic" => CloudLLMProvider.Anthropic(
                _settings.AnthropicApiKey ?? "",
                _loggerFactory.CreateLogger<CloudLLMProvider>()),
            "gemini" => CloudLLMProvider.Gemini(
                _settings.GeminiApiKey ?? "",
                _loggerFactory.CreateLogger<CloudLLMProvider>()),
            _ => new OllamaProvider(
                "http://localhost:11434",
                "qwen2.5-coder:14b",
                _loggerFactory.CreateLogger<OllamaProvider>())
        };
    }

    public string GetRecommendation(List<LLMModelInfo> models)
    {
        if (!models.Any()) return "No models detected. Install Ollama or LM Studio, or add a cloud API key.";

        var bestLocal = models
            .Where(m => m.IsLoaded && (m.Provider == "ollama" || m.Provider == "lmstudio"))
            .OrderByDescending(m => m.SizeBytes ?? 0)
            .FirstOrDefault();

        if (bestLocal != null)
        {
            var sizeGB = bestLocal.SizeBytes.HasValue ? bestLocal.SizeBytes.Value / 1_073_741_824.0 : 0;
            return sizeGB >= 7
                ? $"Recommended: {bestLocal.DisplayName} — large enough for quality resume tailoring"
                : $"Available: {bestLocal.DisplayName} — works but larger models produce better results";
        }

        return "No local models loaded. Pull a model in Ollama (e.g., `ollama pull qwen2.5-coder:14b`)";
    }

    private async Task<LLMConnectionStatus> CheckProviderAsync(ILLMProvider provider)
    {
        var status = new LLMConnectionStatus
        {
            ProviderId = provider.ProviderId,
            ProviderName = provider.DisplayName,
        };

        try
        {
            status.IsConnected = await provider.IsAvailableAsync();
            if (status.IsConnected)
            {
                status.Models = await provider.ListModelsAsync();
                foreach (var m in status.Models)
                    m.Recommendation = GetModelRecommendation(m);
                _log.LogInformation("{Provider}: Connected, {Count} models available", provider.DisplayName, status.Models.Count);
            }
            else
            {
                status.Error = "Not running or not reachable";
                _log.LogInformation("{Provider}: Not available", provider.DisplayName);
            }
        }
        catch (Exception ex)
        {
            status.IsConnected = false;
            status.Error = ex.Message;
            _log.LogWarning(ex, "{Provider}: Connection error", provider.DisplayName);
        }

        return status;
    }

    private static string GetModelRecommendation(LLMModelInfo model)
    {
        var sizeGB = model.SizeBytes.HasValue ? model.SizeBytes.Value / 1_073_741_824.0 : 0;
        var id = model.Id.ToLowerInvariant();

        if (id.Contains("coder") || id.Contains("code"))
            return "Great for code & resume analysis";

        if (sizeGB >= 14)
            return "Excellent quality for resume tailoring";

        if (sizeGB >= 7)
            return "Good balance of quality and speed";

        if (sizeGB >= 3)
            return "Adequate but may produce lower quality results";

        return "Small model — consider upgrading for better results";
    }
}