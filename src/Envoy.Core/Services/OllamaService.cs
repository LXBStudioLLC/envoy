using Microsoft.Extensions.AI;
using System.Text.Json;

namespace Envoy.Core.Services;

public class OllamaService : IDisposable
{
    private readonly IChatClient _chatClient;
    private readonly string _modelName;
    private bool _disposed;

    public OllamaService(string modelName = "qwen2.5-coder:14b", string endpoint = "http://localhost:11434")
    {
        _modelName = modelName;
        _chatClient = new OllamaChatClient(new Uri(endpoint), modelName);
    }

    public async Task<string> CompleteAsync(string prompt, string systemPrompt = "", CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new ChatMessage(ChatRole.System, systemPrompt));
        }
        messages.Add(new ChatMessage(ChatRole.User, prompt));

        var response = await _chatClient.CompleteAsync(messages, cancellationToken: ct);
        return response.Message.Text ?? string.Empty;
    }

    public async Task<T?> CompleteJsonAsync<T>(string prompt, string systemPrompt = "", CancellationToken ct = default)
    {
        var text = await CompleteAsync(prompt, systemPrompt, ct);
        text = ExtractJson(text);
        try
        {
            return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0) return text;

        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}') depth--;
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
            (_chatClient as IDisposable)?.Dispose();
        }
    }
}