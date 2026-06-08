namespace Envoy.Core.Services;

public interface ILLMProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    string Description { get; }
    bool RequiresApiKey { get; }
    bool IsLocal { get; }

    Task<bool> IsAvailableAsync();
    Task<List<LLMModelInfo>> ListModelsAsync();
    Task<string> CompleteAsync(string prompt, string systemPrompt, string? modelId, CancellationToken ct = default);
    Task<T?> CompleteJsonAsync<T>(string prompt, string systemPrompt, string? modelId, CancellationToken ct = default);
}

public class LLMModelInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "";
    public long? SizeBytes { get; set; }
    public string? Quantization { get; set; }
    public string? Family { get; set; }
    public bool IsLoaded { get; set; }
    public double? VramRequirementMB { get; set; }
    public string Recommendation { get; set; } = "";

    public string DisplayName => string.IsNullOrEmpty(Name) ? Id : Name;

    public string SizeDisplay
    {
        get
        {
            if (SizeBytes == null || SizeBytes == 0) return "Unknown";
            var gb = SizeBytes.Value / 1_073_741_824.0;
            return $"{gb:F1} GB";
        }
    }
}

public class LLMConnectionStatus
{
    public string ProviderId { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public bool IsConnected { get; set; }
    public string Endpoint { get; set; } = "";
    public List<LLMModelInfo> Models { get; set; } = new();
    public string? Error { get; set; }
}