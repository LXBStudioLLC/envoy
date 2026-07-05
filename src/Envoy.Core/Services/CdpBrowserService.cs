using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Envoy.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Envoy.Core.Services;

public class CdpBrowserService : ICdpCommandExecutor, IPageInteractor, IBrowserLifecycle, IAsyncDisposable, IDisposable
{
    private ClientWebSocket? _webSocket;
    private int _messageId = 0;
    private string? _sessionId;
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pendingCommands = new();
    // Multiple callers may legitimately wait for the same event (e.g. concurrent
    // multi-step nav). Store a list of TCSs and complete-all on arrival.
    private readonly Dictionary<string, List<TaskCompletionSource<JsonElement>>> _pendingEvents = new();
    private readonly HumanizationService _humanization;
    private readonly EnvoySettings _settings;
    private readonly ILogger<CdpBrowserService> _log;
    private readonly object _lock = new();
    private Task? _receiveLoopTask;
    private readonly CancellationTokenSource _receiveCts = new();
    private bool _disposed;

    public bool IsConnected => _webSocket?.State == WebSocketState.Open;

    public CdpBrowserService(HumanizationService humanization, EnvoySettings settings, ILogger<CdpBrowserService> log)
    {
        _humanization = humanization;
        _settings = settings;
        _log = log;
    }

    public async Task<bool> ConnectAsync(int port = 9222, CancellationToken ct = default)
    {
        try
        {
            if (_webSocket?.State == WebSocketState.Open)
                return true;

            await CloseAsync(ct);

            _webSocket = new ClientWebSocket();
            var uri = new Uri($"ws://localhost:{port}/devtools/browser");
            await _webSocket.ConnectAsync(uri, ct);

            _receiveLoopTask = ReceiveLoop(_receiveCts.Token);
            _log.LogInformation("Connected to Chrome CDP on port {Port}", port);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to connect to Chrome CDP on port {Port}", port);
            return false;
        }
    }

    public async Task<string?> CreatePageAsync(CancellationToken ct = default)
    {
        var result = await SendCommandAsync("Target.createTarget", new { url = "about:blank" }, ct);
        return result.TryGetProperty("targetId", out var tid) ? tid.GetString() : null;
    }

    public async Task<bool> AttachToPageAsync(string targetId, CancellationToken ct = default)
    {
        var result = await SendCommandAsync("Target.attachToTarget", new { targetId, flatten = true }, ct);
        if (result.TryGetProperty("sessionId", out var sid))
        {
            _sessionId = sid.GetString();
            _log.LogInformation("Attached to page session {SessionId}", _sessionId);
            // Enable the Page domain so navigation lifecycle events (Page.loadEventFired)
            // are dispatched. Without this, NavigateAsync's wait always times out.
            await SendCommandAsync("Page.enable", new { }, ct);
            return true;
        }
        _log.LogWarning("Failed to attach to page - no sessionId in response");
        return false;
    }

    public async Task CloseAsync(CancellationToken ct = default)
    {
        _sessionId = null;
        if (_webSocket != null)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", ct);
            }
            catch { }
            _webSocket.Dispose();
            _webSocket = null;
        }
    }

    public async Task NavigateAsync(string url, CancellationToken ct = default)
    {
        // Register the load-event waiter BEFORE navigating so a fast page load can't
        // fire Page.loadEventFired between the navigate ack and the wait registration.
        // (The Page domain is enabled in AttachToPageAsync so the event is dispatched.)
        var loaded = WaitForEventAsync("Page.loadEventFired", TimeSpan.FromSeconds(30), ct);
        await SendCommandAsync("Page.navigate", new { url }, ct);
        await loaded;
    }

    public async Task<string?> QuerySelectorAsync(string selector, CancellationToken ct = default)
    {
        var documentResult = await SendCommandAsync("DOM.getDocument", new { }, ct);
        int rootNodeId = 1;
        if (documentResult.TryGetProperty("root", out var root) && root.TryGetProperty("nodeId", out var rid))
            rootNodeId = rid.GetInt32();

        var result = await SendCommandAsync("DOM.querySelector", new { nodeId = rootNodeId, selector }, ct);
        if (result.TryGetProperty("nodeId", out var nodeId))
        {
            var id = nodeId.GetInt32();
            return id == 0 ? null : id.ToString();
        }
        return null;
    }

    public async Task<List<string>> QuerySelectorAllAsync(string selector, CancellationToken ct = default)
    {
        var documentResult = await SendCommandAsync("DOM.getDocument", new { }, ct);
        int rootNodeId = 1;
        if (documentResult.TryGetProperty("root", out var root) && root.TryGetProperty("nodeId", out var rid))
            rootNodeId = rid.GetInt32();

        var result = await SendCommandAsync("DOM.querySelectorAll", new { nodeId = rootNodeId, selector }, ct);
        var ids = new List<string>();
        if (result.TryGetProperty("nodeIds", out var nodeIds))
        {
            foreach (var id in nodeIds.EnumerateArray())
            {
                ids.Add(id.GetInt32().ToString());
            }
        }
        return ids;
    }

    public async Task FocusAsync(string nodeId, CancellationToken ct = default)
    {
        await SendCommandAsync("DOM.focus", new { nodeId = int.Parse(nodeId) }, ct);
    }

    public async Task TypeTextAsync(string nodeId, string text, CancellationToken ct = default)
    {
        await FocusAsync(nodeId, ct);

        foreach (var ch in text)
        {
            // Per-keystroke human-cadence jitter only when stealth input is enabled.
            if (_settings.StealthModeEnabled)
                await Task.Delay(_humanization.GetTypingDelay(), ct);

            string code;
            int vkCode;
            if (char.IsLetter(ch))
            {
                code = $"Key{char.ToUpper(ch)}";
                vkCode = (int)char.ToUpper(ch);
            }
            else if (char.IsDigit(ch))
            {
                code = $"Digit{ch}";
                vkCode = (int)ch;
            }
            else
            {
                code = ch switch
                {
                    ' ' => "Space",
                    '.' => "Period",
                    ',' => "Comma",
                    '@' => "Digit2",
                    '-' => "Minus",
                    '_' => "Minus",
                    '\t' => "Tab",
                    '\n' => "Enter",
                    '\r' => "Enter",
                    _ => ch.ToString()
                };
                vkCode = (int)ch;
            }

            await SendCommandAsync("Input.dispatchKeyEvent", new
            {
                type = "keyDown",
                text = ch.ToString(),
                unmodifiedText = ch.ToString(),
                code,
                key = ch.ToString(),
                windowsVirtualKeyCode = vkCode,
                nativeVirtualKeyCode = vkCode
            }, ct);

            await SendCommandAsync("Input.dispatchKeyEvent", new
            {
                type = "keyUp",
                code,
                key = ch.ToString(),
                windowsVirtualKeyCode = vkCode,
                nativeVirtualKeyCode = vkCode
            }, ct);
        }
    }

    public async Task ClickAsync(string nodeId, CancellationToken ct = default)
    {
        var box = await GetBoundingBoxAsync(nodeId, ct);
        if (box == null) return;

        if (!_settings.StealthModeEnabled)
        {
            // Plain click at the element center — no synthetic human movement or jitter.
            var px = box.Value.x + box.Value.width / 2;
            var py = box.Value.y + box.Value.height / 2;
            await SendCommandAsync("Input.dispatchMouseEvent", new { type = "mouseMoved", x = px, y = py }, ct);
            await SendCommandAsync("Input.dispatchMouseEvent", new { type = "mousePressed", x = px, y = py, button = "left", clickCount = 1 }, ct);
            await SendCommandAsync("Input.dispatchMouseEvent", new { type = "mouseReleased", x = px, y = py, button = "left", clickCount = 1 }, ct);
            return;
        }

        // Stealth (gated by StealthModeEnabled): human-cadence movement along a Bezier path.
        var (x, y) = _humanization.GetClickTarget(box.Value.x, box.Value.y, box.Value.width, box.Value.height);
        var path = _humanization.GenerateMousePath(0, 0, x, y);

        foreach (var point in path)
        {
            await SendCommandAsync("Input.dispatchMouseEvent", new
            {
                type = "mouseMoved",
                x = point.X,
                y = point.Y
            }, ct);
            await Task.Delay(_humanization.GetMicroDelay(), ct);
        }

        await Task.Delay(_humanization.GetClickDelay(), ct);

        await SendCommandAsync("Input.dispatchMouseEvent", new
        {
            type = "mousePressed",
            x = path.Last().X,
            y = path.Last().Y,
            button = "left",
            clickCount = 1
        }, ct);

        await Task.Delay(_humanization.GetMicroDelay(), ct);

        await SendCommandAsync("Input.dispatchMouseEvent", new
        {
            type = "mouseReleased",
            x = path.Last().X,
            y = path.Last().Y,
            button = "left",
            clickCount = 1
        }, ct);
    }

    public async Task SetFileInputAsync(string nodeId, string filePath, CancellationToken ct = default)
    {
        await SendCommandAsync("DOM.setFileInputFiles", new
        {
            nodeId = int.Parse(nodeId),
            files = new[] { filePath }
        }, ct);
    }

    public async Task<string> GetPageTextAsync(CancellationToken ct = default)
    {
        var result = await SendCommandAsync("Runtime.evaluate", new
        {
            expression = "document.body.innerText",
            returnByValue = true
        }, ct);

        if (result.TryGetProperty("result", out var res) && res.TryGetProperty("value", out var val))
        {
            return val.GetString() ?? "";
        }
        return "";
    }

    public async Task<byte[]> CaptureScreenshotAsync(CancellationToken ct = default)
    {
        var result = await SendCommandAsync("Page.captureScreenshot", new { format = "png" }, ct);
        if (result.TryGetProperty("data", out var data))
        {
            return Convert.FromBase64String(data.GetString() ?? "");
        }
        return Array.Empty<byte>();
    }

    public async Task<bool> DetectCaptchaAsync(CancellationToken ct = default)
    {
        var captchaSelectors = new[]
        {
            "iframe[src*='recaptcha']",
            "iframe[src*='hcaptcha']",
            ".g-recaptcha",
            ".h-captcha",
            "input[name*='captcha']",
            "#captcha"
        };

        foreach (var selector in captchaSelectors)
        {
            var nodeId = await QuerySelectorAsync(selector, ct);
            if (nodeId != null) return true;
        }

        var pageText = await GetPageTextAsync(ct);
        var captchaKeywords = new[] { "captcha", "verify you are human", "i'm not a robot", "security check" };
        if (captchaKeywords.Any(k => pageText.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    public async Task<JsonElement> SendCommandAsync(string method, object? parameters, CancellationToken ct = default)
    {
        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket not connected");

        var id = Interlocked.Increment(ref _messageId);
        var tcs = new TaskCompletionSource<JsonElement>();

        lock (_lock)
        {
            _pendingCommands[id] = tcs;
        }

        var messageDict = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters
        };
        if (!string.IsNullOrEmpty(_sessionId))
            messageDict["sessionId"] = _sessionId;

        var json = JsonSerializer.Serialize(messageDict);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            return await tcs.Task.WaitAsync(cts.Token);
        }
        finally
        {
            lock (_lock)
            {
                _pendingCommands.Remove(id);
            }
        }
    }

    public async Task<T?> SendCommandAsync<T>(string method, object? parameters, CancellationToken ct = default)
    {
        var result = await SendCommandAsync(method, parameters, ct);
        return JsonSerializer.Deserialize<T>(result.GetRawText());
    }

    public async Task WaitForEventAsync(string eventName, TimeSpan timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<JsonElement>();

        lock (_lock)
        {
            if (!_pendingEvents.TryGetValue(eventName, out var list))
            {
                list = new List<TaskCompletionSource<JsonElement>>();
                _pendingEvents[eventName] = list;
            }
            list.Add(tcs);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            await tcs.Task.WaitAsync(cts.Token);
        }
        finally
        {
            lock (_lock)
            {
                if (_pendingEvents.TryGetValue(eventName, out var list))
                {
                    list.Remove(tcs);
                    if (list.Count == 0) _pendingEvents.Remove(eventName);
                }
            }
        }
    }

    private async Task<(double x, double y, double width, double height)?> GetBoundingBoxAsync(string nodeId, CancellationToken ct)
    {
        var result = await SendCommandAsync("DOM.getBoxModel", new { nodeId = int.Parse(nodeId) }, ct);
        if (result.TryGetProperty("model", out var model) && model.TryGetProperty("content", out var content))
        {
            var coords = content.EnumerateArray().Select(v => v.GetDouble()).ToArray();
            if (coords.Length >= 8)
            {
                var x = coords[0];
                var y = coords[1];
                var width = coords[2] - coords[0];
                var height = coords[5] - coords[1];
                return (x, y, width, height);
            }
        }
        return null;
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[131072];
        var messageBuilder = new MemoryStream();

        while (!ct.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
        {
            try
            {
                messageBuilder.SetLength(0);
                WebSocketReceiveResult result;

                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;
                    messageBuilder.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var message = Encoding.UTF8.GetString(messageBuilder.GetBuffer(), 0, (int)messageBuilder.Length);
                JsonDocument doc;
                try { doc = JsonDocument.Parse(message); } catch { continue; }
                var root = doc.RootElement;

                if (root.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var msgId))
                {
                    lock (_lock)
                    {
                        if (_pendingCommands.TryGetValue(msgId, out var tcs))
                        {
                            if (root.TryGetProperty("result", out var res))
                                tcs.TrySetResult(res);
                            else if (root.TryGetProperty("error", out var err))
                                tcs.TrySetException(new InvalidOperationException(err.ToString()));
                        }
                    }
                }
                else if (root.TryGetProperty("method", out var methodProp))
                {
                    var method = methodProp.GetString() ?? "";
                    List<TaskCompletionSource<JsonElement>>? waiters = null;
                    lock (_lock)
                    {
                        if (_pendingEvents.TryGetValue(method, out var list) && list.Count > 0)
                        {
                            waiters = new List<TaskCompletionSource<JsonElement>>(list);
                            // WaitForEventAsync's finally clause will remove the entries
                            // it owns; we only consume the snapshot here.
                        }
                    }
                    if (waiters != null)
                    {
                        foreach (var w in waiters)
                            w.TrySetResult(root);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "CDP receive loop error");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _receiveCts.Cancel();
        if (_receiveLoopTask != null)
        {
            try { await _receiveLoopTask; } catch { }
        }
        await CloseAsync(CancellationToken.None);
        _receiveCts.Dispose();
    }

    // Synchronous disposal for DI containers that don't honor IAsyncDisposable
    // (Microsoft.Extensions.DependencyInjection does, but defense in depth).
    // Runs the async path with GetAwaiter().GetResult(); safe at app shutdown
    // when no SynchronizationContext is captured.
    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "CdpBrowserService synchronous dispose failed");
        }
    }
}