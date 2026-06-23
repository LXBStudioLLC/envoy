using Envoy.Discovery.Sources;
using Xunit;

namespace Envoy.Discovery.Tests;

public class BraveSearchSourceTests
{
    private const string SampleJson = """
    { "web": { "results": [ {
        "title": "Senior Rust Engineer at Acme",
        "url": "https://boards.greenhouse.io/acme/jobs/9",
        "description": "A <strong>great</strong> remote role",
        "page_age": "2026-06-01T00:00:00Z",
        "meta_url": { "hostname": "boards.greenhouse.io" }
    } ] } }
    """;

    [Fact]
    public async Task Search_ParsesResultsAndStripsHighlightTags()
    {
        var source = new BraveSearchSource(StubHttpMessageHandler.Client(SampleJson));

        var job = Assert.Single(await source.SearchAsync("test-key", "rust engineer", 10));

        Assert.Equal("Senior Rust Engineer at Acme", job.JobTitle);
        Assert.Equal("https://boards.greenhouse.io/acme/jobs/9", job.Url);
        Assert.Equal("A great remote role", job.DescriptionText); // <strong> stripped
        Assert.Equal("boards.greenhouse.io", job.CompanyName);
        Assert.NotNull(job.PostedAtUtc);
    }

    [Fact]
    public async Task Search_SendsSubscriptionTokenHeader()
    {
        var handler = new StubHttpMessageHandler(SampleJson);
        var source = new BraveSearchSource(new HttpClient(handler));

        await source.SearchAsync("secret-key", "designer", 5);

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.TryGetValues("X-Subscription-Token", out var values));
        Assert.Equal("secret-key", Assert.Single(values!));
        Assert.Contains("q=designer", handler.LastRequest.RequestUri!.Query);
    }

    [Fact]
    public async Task Search_EmptyApiKey_Throws()
    {
        var source = new BraveSearchSource(StubHttpMessageHandler.Client(SampleJson));
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.SearchAsync("", "x", 5));
    }
}
