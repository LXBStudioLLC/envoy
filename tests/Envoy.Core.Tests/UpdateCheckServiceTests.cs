using System.Net;
using Envoy.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Envoy.Core.Tests;

public class UpdateCheckServiceTests
{
    private const string NewerReleaseJson =
        """{"tag_name":"v9.9.9","html_url":"https://github.com/LXBStudioLLC/envoy/releases/tag/v9.9.9"}""";

    private static (UpdateCheckService Service, StubHandler Handler) Create(
        string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body)
        });
        var service = new UpdateCheckService(new HttpClient(handler), NullLogger<UpdateCheckService>.Instance);
        return (service, handler);
    }

    [Fact]
    public async Task NewerRelease_ReturnsVersionAndUrl()
    {
        var (service, _) = Create(NewerReleaseJson);

        var update = await service.CheckForUpdateAsync("1.0.3");

        Assert.NotNull(update);
        Assert.Equal("9.9.9", update.LatestVersion);
        Assert.Equal("https://github.com/LXBStudioLLC/envoy/releases/tag/v9.9.9", update.ReleaseUrl);
    }

    [Fact]
    public async Task SameVersion_ReturnsNull()
    {
        var (service, _) = Create(
            """{"tag_name":"v1.0.3","html_url":"https://github.com/LXBStudioLLC/envoy/releases/tag/v1.0.3"}""");

        Assert.Null(await service.CheckForUpdateAsync("1.0.3"));
    }

    [Fact]
    public async Task OlderRelease_ReturnsNull()
    {
        var (service, _) = Create(
            """{"tag_name":"v1.0.2","html_url":"https://github.com/LXBStudioLLC/envoy/releases/tag/v1.0.2"}""");

        Assert.Null(await service.CheckForUpdateAsync("1.0.3"));
    }

    [Fact]
    public async Task TagWithoutLeadingV_StillCompares()
    {
        var (service, _) = Create(
            """{"tag_name":"2.0.0","html_url":"https://github.com/LXBStudioLLC/envoy/releases/tag/2.0.0"}""");

        var update = await service.CheckForUpdateAsync("1.0.3");

        Assert.NotNull(update);
        Assert.Equal("2.0.0", update.LatestVersion);
    }

    [Fact]
    public async Task CurrentVersionWithCommitHash_IsNormalized()
    {
        var (service, _) = Create(NewerReleaseJson);

        var update = await service.CheckForUpdateAsync("1.0.3+abc123");

        Assert.NotNull(update);
    }

    [Fact]
    public async Task HtmlUrlOutsideRepo_FallsBackToReleasesPage()
    {
        var (service, _) = Create(
            """{"tag_name":"v9.9.9","html_url":"https://evil.example.com/download"}""");

        var update = await service.CheckForUpdateAsync("1.0.3");

        Assert.NotNull(update);
        Assert.Equal("https://github.com/LXBStudioLLC/envoy/releases/latest", update.ReleaseUrl);
    }

    [Fact]
    public async Task HttpError_ReturnsNull()
    {
        var (service, _) = Create("rate limited", HttpStatusCode.Forbidden);

        Assert.Null(await service.CheckForUpdateAsync("1.0.3"));
    }

    [Fact]
    public async Task MalformedJson_ReturnsNull()
    {
        var (service, _) = Create("<!doctype html>");

        Assert.Null(await service.CheckForUpdateAsync("1.0.3"));
    }

    [Fact]
    public async Task MissingTagName_ReturnsNull()
    {
        var (service, _) = Create("""{"html_url":"https://github.com/LXBStudioLLC/envoy"}""");

        Assert.Null(await service.CheckForUpdateAsync("1.0.3"));
    }

    [Fact]
    public async Task UnparseableTag_ReturnsNull()
    {
        var (service, _) = Create("""{"tag_name":"nightly"}""");

        Assert.Null(await service.CheckForUpdateAsync("1.0.3"));
    }

    [Fact]
    public async Task UnparseableCurrentVersion_ReturnsNull()
    {
        var (service, handler) = Create(NewerReleaseJson);

        Assert.Null(await service.CheckForUpdateAsync("not-a-version"));
        Assert.Null(handler.LastRequest); // bails out before making any request
    }

    [Fact]
    public async Task NetworkFailure_ReturnsNull()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("offline"));
        var service = new UpdateCheckService(new HttpClient(handler), NullLogger<UpdateCheckService>.Instance);

        Assert.Null(await service.CheckForUpdateAsync("1.0.3"));
    }

    [Fact]
    public async Task SendsUserAgentAndAcceptHeaders()
    {
        var (service, handler) = Create(NewerReleaseJson);

        await service.CheckForUpdateAsync("1.0.3");

        Assert.NotNull(handler.LastRequest);
        Assert.Contains("Envoy", handler.LastRequest!.Headers.UserAgent.ToString());
        Assert.Contains("application/vnd.github+json", handler.LastRequest.Headers.Accept.ToString());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_respond(request));
        }
    }
}
