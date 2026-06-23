using System.Net;
using System.Text;

namespace Envoy.Discovery.Tests;

/// <summary>
/// Test handler that answers requests from canned strings — no network. Records the
/// last request so tests can assert on the URL and headers that were sent.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, (HttpStatusCode Code, string Body)> _responder;

    public HttpRequestMessage? LastRequest { get; private set; }

    public StubHttpMessageHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> responder) => _responder = responder;

    public StubHttpMessageHandler(string body, HttpStatusCode code = HttpStatusCode.OK)
        : this(_ => (code, body)) { }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        var (code, body) = _responder(request);
        return Task.FromResult(new HttpResponseMessage(code)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }

    public static HttpClient Client(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(new StubHttpMessageHandler(body, code));
}
