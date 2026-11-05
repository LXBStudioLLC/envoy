using Envoy.Core.Services;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Envoy.Core.Tests;

/// <summary>
/// APP-13: CdpPageSession — independent per-page CDP sessions. A deterministic
/// fake websocket (server frames signalled through a completion source) proves
/// command routing by id, event waiting, and two concurrent sessions not
/// sharing state. Zero polling, zero races.
/// </summary>
public sealed class CdpPageSessionTests
{
    /// <summary>
    /// Scripted websocket: the test queues server frames (responses/events)
    /// via Feed; Send frames surface in SentFrames and can auto-reply.
    /// </summary>
    private sealed class ScriptedWebSocket : WebSocket
    {
        private readonly Queue<WebSocketReceiveResult> _results = new();
        private readonly Queue<byte[]> _payloads = new();

        private readonly object _sync = new();
        private TaskCompletionSource<WebSocketReceiveResult>? _pendingReceive;
        private bool _closed;

        public readonly List<string> SentFrames = new();
        public Func<string, string?> OnCommand { get; set; } = static _ => null;

        public override WebSocketState State => _closed ? WebSocketState.Closed : WebSocketState.Open;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override string? SubProtocol => null;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
        {
            lock (_sync)
            {
                if (_results.Count > 0)
                {
                    return Task.FromResult(ReadInto(buffer));
                }

                _pendingReceive ??= new TaskCompletionSource<WebSocketReceiveResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                return _pendingReceive.Task;
            }
        }

        private WebSocketReceiveResult ReadInto(ArraySegment<byte> buffer)
        {
            var payload = _payloads.Dequeue();
            var result = _results.Dequeue();
            var count = Math.Min(payload.Length, buffer.Count);
            System.Array.Copy(payload, buffer.Array!, count);
            return new WebSocketReceiveResult(count, WebSocketMessageType.Text, result.EndOfMessage);
        }

        private void EnqueueFrame(string json, WebSocketReceiveResult result)
        {
            TaskCompletionSource<WebSocketReceiveResult>? waiter;
            lock (_sync)
            {
                _payloads.Enqueue(Encoding.UTF8.GetBytes(json));
                _results.Enqueue(result);
                waiter = _pendingReceive;
                _pendingReceive = null;
            }
            waiter?.TrySetResult(result);
        }

        public void Feed(string json) =>
            EnqueueFrame(json, new WebSocketReceiveResult(Encoding.UTF8.GetBytes(json).Length, WebSocketMessageType.Text, true));

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            var frame = Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count);
            lock (SentFrames) SentFrames.Add(frame);

            var reply = OnCommand(frame);
            if (reply is not null) Feed(reply);
            return Task.CompletedTask;
        }

        public override void Abort() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) { _closed = true; return Task.CompletedTask; }
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Dispose() { _closed = true; }
    }

    private static string ResponseForId(string sentFrame, string value)
    {
        var node = System.Text.Json.JsonDocument.Parse(sentFrame).RootElement;
        var id = node.GetProperty("id").GetInt32();
        // CDP Runtime.evaluate shape: { "id":N, "result":{ "type":"string", "value":"…" } }
        return "{\"id\":" + id + ",\"result\":{\"value\":\"" + value + "\"}}";
    }

    [Fact]
    public async Task SendAsync_RoutesResponseToCaller_ById()
    {
        var ws = new ScriptedWebSocket();
        var session = CdpPageSession.AttachForTests(ws, "T1");

        ws.OnCommand = frame =>
            frame.Contains("\"method\":\"Runtime.evaluate\"")
                ? ResponseForId(frame, "page text")
                : null;

        var result = await session.SendAsync("Runtime.evaluate", new JsonObject(), CancellationToken.None);

        var value = result.GetProperty("result").GetProperty("value").GetString();
        Assert.Equal("page text", value);
    }

    [Fact]
    public async Task WaitForEventAsync_ResolvesOnInjectedFrame()
    {
        var ws = new ScriptedWebSocket();
        var session = CdpPageSession.AttachForTests(ws, "T1");

        var waiter = session.WaitForEventAsync("Page.loadEventFired", TimeSpan.FromSeconds(2), CancellationToken.None);
        ws.Feed("{\"method\":\"Page.loadEventFired\",\"params\":{}}");

        var frame = await waiter;
        Assert.Equal("Page.loadEventFired", frame.GetProperty("method").GetString());
    }

    [Fact]
    public async Task NavigateAsync_SendsNavigate_AndWaitsEvent()
    {
        var ws = new ScriptedWebSocket();

        // Every navigate command gets BOTH a response (routing by id) and an
        // injected load event — the exact real-page behavior.
        ws.OnCommand = frame =>
        {
            if (frame.Contains("\"method\":\"Page.navigate\""))
            {
                var id = System.Text.Json.JsonDocument.Parse(frame).RootElement.GetProperty("id").GetInt32();
                ws.Feed("{\"id\":" + id + ",\"result\":{}}");
                ws.Feed("{\"method\":\"Page.loadEventFired\",\"params\":{}}");
            }
            return null;
        };

        var session = CdpPageSession.AttachForTests(ws, "T1");
        await session.NavigateAsync("https://x.com/j");

        Assert.Contains(ws.SentFrames, f => f.Contains("Page.navigate") && f.Contains("https://x.com/j"));
    }

    [Fact]
    public async Task DetectCaptcha_FalseOnCleanText_TrueOnCaptchaPage()
    {
        var ws = new ScriptedWebSocket();
        var session = CdpPageSession.AttachForTests(ws, "T1");

        ws.OnCommand = frame => ResponseForId(frame, "all good here");
        Assert.False(await session.DetectCaptchaAsync());

        ws.OnCommand = frame => ResponseForId(frame, "Please verify you are human");
        Assert.True(await session.DetectCaptchaAsync());
    }

    [Fact]
    public async Task TwoSessions_NeverShareState()
    {
        var ws1 = new ScriptedWebSocket();
        var ws2 = new ScriptedWebSocket();
        var s1 = CdpPageSession.AttachForTests(ws1, "T1");
        var s2 = CdpPageSession.AttachForTests(ws2, "T2");

        ws1.OnCommand = frame => ResponseForId(frame, "from-1");
        ws2.OnCommand = frame => ResponseForId(frame, "from-2");

        var r1 = await s1.SendAsync("Runtime.evaluate", new JsonObject(), CancellationToken.None);
        var r2 = await s2.SendAsync("Runtime.evaluate", new JsonObject(), CancellationToken.None);

        Assert.Equal("from-1", r1.GetProperty("result").GetProperty("value").GetString());
        Assert.Equal("from-2", r2.GetProperty("result").GetProperty("value").GetString());
    }
}
