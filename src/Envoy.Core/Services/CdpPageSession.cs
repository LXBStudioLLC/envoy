using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Envoy.Core.Services;

/// <summary>
/// One CDP page session bound to its own page-level websocket
/// (ws://localhost:{port}/devtools/page/{targetId}) — APP-13. Unlike the
/// browser-authority singleton, sessions are independent: command ids, event
/// waiters, and the attached target never collide across sessions, so parallel
/// queue workers may drive multiple tabs safely.
///
/// <para>The websocket transport is injectable for offline tests; production
/// uses a plain ClientWebSocket against the debug port's page endpoint.</para>
/// </summary>
public sealed class CdpPageSession : IAsyncDisposable
{
    private readonly WebSocket _ws;
    private readonly string _targetId;
    private readonly ILogger? _log;
    private int _nextMessageId = 1;

    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly List<(string EventName, TaskCompletionSource<JsonElement> Tcs)> _eventWaiters = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource _receiveCts = new();

    public string TargetId => _targetId;
    public bool IsConnected => _ws.State == WebSocketState.Open;

    private CdpPageSession(WebSocket ws, string targetId, ILogger? log)
    {
        _ws = ws;
        _targetId = targetId;
        _log = log;
    }

    /// <summary>Attaches to a page target with its own websocket + enables the Page domain.</summary>
    public static async Task<CdpPageSession> AttachAsync(
        int port, string targetId,
        Func<Uri, CancellationToken, Task<WebSocket>>? transport = null,
        ILogger? log = null,
        CancellationToken ct = default)
    {
        transport ??= static async (uri, token) =>
        {
            var ws = new ClientWebSocket();
            await ws.ConnectAsync(uri, token);
            return ws;
        };

        var ws = await transport(new Uri($"ws://localhost:{port}/devtools/page/{targetId}"), ct);
        var session = new CdpPageSession(ws, targetId, log);
        session.StartReceiveLoop();
        await session.SendAsync("Page.enable", new JsonObject(), ct);
        return session;
    }

    /// <summary>Lets offline tests stub the websocket transport.</summary>
    public static CdpPageSession AttachForTests(WebSocket stub, string targetId, ILogger? log = null)
    {
        var session = new CdpPageSession(stub, targetId, log);
        session.StartReceiveLoop();
        return session;
    }

    private void StartReceiveLoop() => _ = ReceiveLoop(_receiveCts.Token);

    private int NextId() => Interlocked.Increment(ref _nextMessageId);

    public async Task<JsonElement> SendAsync(string method, JsonObject? parameters, CancellationToken ct)
    {
        var id = NextId();
        var node = new JsonObject
        {
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters ?? new JsonObject()
        };
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending) _pending[id] = tcs;

        var msg = node.ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(msg);
        await _sendLock.WaitAsync(ct);
        try
        {
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            _sendLock.Release();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        return await tcs.Task.WaitAsync(timeout.Token);
    }

    public async Task<JsonElement> WaitForEventAsync(string eventName, TimeSpan timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_eventWaiters) _eventWaiters.Add((eventName, tcs));
        try
        {
            return await tcs.Task.WaitAsync(timeout, ct);
        }
        finally
        {
            lock (_eventWaiters) _eventWaiters.RemoveAll(w => w.Tcs == tcs);
        }
    }

    public async Task NavigateAsync(string url, CancellationToken ct = default)
    {
        var waiter = WaitForEventAsync("Page.loadEventFired", TimeSpan.FromSeconds(30), ct);
        await SendAsync("Page.navigate", new JsonObject { ["url"] = url }, ct);
        try { await waiter; }
        catch (TimeoutException) { /* long page: the navigate still went out */ }
    }

    public async Task<string> GetPageTextAsync(CancellationToken ct = default)
    {
        var result = await SendAsync("Runtime.evaluate", new JsonObject
        {
            ["expression"] = "document.body.innerText",
            ["returnByValue"] = true
        }, ct);
        return result.TryGetProperty("result", out var r) && r.TryGetProperty("value", out var v)
            ? v.GetString() ?? ""
            : "";
    }

    public async Task<bool> DetectCaptchaAsync(CancellationToken ct = default)
    {
        var text = await GetPageTextAsync(ct);
        return text.Contains("captcha", StringComparison.OrdinalIgnoreCase)
            || text.Contains("verify you are human", StringComparison.OrdinalIgnoreCase)
            || text.Contains("i'm not a robot", StringComparison.OrdinalIgnoreCase);
    }

    public async ValueTask DisposeAsync()
    {
        _receiveCts.Cancel();
        try { if (_ws.State == WebSocketState.Open) await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); }
        catch { }
        _ws.Dispose();
        _receiveCts.Dispose();
        _sendLock.Dispose();
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[1 << 18];
        var sb = new StringBuilder();
        try
        {
            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                sb.Clear();
                WebSocketReceiveResult read;
                do
                {
                    read = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (read.MessageType == WebSocketMessageType.Close)
                        return;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, read.Count));
                } while (!read.EndOfMessage);

                await HandleMessageAsync(sb.ToString());
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "CdpPageSession receive loop died");
        }
    }

    private async Task HandleMessageAsync(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var node = doc.RootElement;
            var id = node.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : default(int?);
            if (id is not null && _pending.Remove(id.Value, out var cmdTcs))
            {
                cmdTcs.TrySetResult(node.Clone());
                return;
            }
            if (node.TryGetProperty("method", out var method))
            {
                lock (_eventWaiters)
                {
                    foreach (var w in _eventWaiters.Where(w => w.EventName == method.GetString()).ToList())
                        w.Tcs.TrySetResult(node.Clone());
                }
            }
        }
        catch (JsonException)
        {
            // malformed frames arrive rarely at the wrong transport stubs; ignore
        }
        await Task.CompletedTask;
    }
}
