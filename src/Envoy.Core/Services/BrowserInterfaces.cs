using System.Text.Json;
using System.Text.Json.Nodes;

namespace Envoy.Core.Services;

public interface ICdpCommandExecutor
{
    Task<JsonElement> SendCommandAsync(string method, object? parameters, CancellationToken ct = default);
    Task<T?> SendCommandAsync<T>(string method, object? parameters, CancellationToken ct = default);
    Task WaitForEventAsync(string eventName, TimeSpan timeout, CancellationToken ct = default);
}

public interface IPageInteractor
{
    Task<string?> QuerySelectorAsync(string selector, CancellationToken ct = default);
    Task<List<string>> QuerySelectorAllAsync(string selector, CancellationToken ct = default);
    Task FocusAsync(string nodeId, CancellationToken ct = default);
    Task TypeTextAsync(string nodeId, string text, CancellationToken ct = default);
    Task ClickAsync(string nodeId, CancellationToken ct = default);
    Task SetFileInputAsync(string nodeId, string filePath, CancellationToken ct = default);
    Task<string> GetPageTextAsync(CancellationToken ct = default);
    Task<byte[]> CaptureScreenshotAsync(CancellationToken ct = default);
    Task<bool> DetectCaptchaAsync(CancellationToken ct = default);
    Task NavigateAsync(string url, CancellationToken ct = default);
}

public interface IBrowserLifecycle
{
    Task<bool> ConnectAsync(int port = 9222, CancellationToken ct = default);
    Task<string?> CreatePageAsync(CancellationToken ct = default);
    Task<bool> AttachToPageAsync(string targetId, CancellationToken ct = default);
    Task CloseAsync(CancellationToken ct = default);
}
