using Envoy.GhostDetection.Models;
using Envoy.GhostDetection.Signals;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Envoy.GhostDetection.Tests;

public class AtsCrossCheckSignalTests
{
    private static HttpClient MockClient(params (string Url, string Response, HttpStatusCode Status)[] endpoints)
    {
        var handler = new MockHttpMessageHandler(endpoints);
        return new HttpClient(handler);
    }

    [Fact]
    public async Task EvaluateAsync_GreenhouseJobFound_ReturnsLowScore()
    {
        var greenhouseResponse = @"{ ""jobs"": [
            { ""title"": ""Senior Software Engineer"", ""location"": { ""name"": ""San Francisco, CA"" } }
        ] }";

        var client = MockClient((
            "https://boards-api.greenhouse.io/v1/boards/acme/jobs",
            greenhouseResponse,
            HttpStatusCode.OK));

        var signal = new AtsCrossCheckSignal(client);
        var posting = new JobPosting
        {
            Source = JobSource.Greenhouse,
            CompanyName = "Acme Corp",
            JobTitle = "Senior Software Engineer",
            Location = "San Francisco, CA",
            Url = "https://boards.greenhouse.io/acme/jobs/12345"
        };

        var result = await signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.Equal("ATS Cross-Check", result.SignalName);
        Assert.True(result.Score < 0.5, $"Expected low score but got {result.Score}");
        Assert.Contains("Confirmed live", result.Evidence[0]);
    }

    [Fact]
    public async Task EvaluateAsync_GreenhouseJobMissing_ReturnsHighScore()
    {
        var greenhouseResponse = @"{ ""jobs"": [
            { ""title"": ""Junior Designer"", ""location"": { ""name"": ""Austin, TX"" } }
        ] }";

        var client = MockClient((
            "https://boards-api.greenhouse.io/v1/boards/acme/jobs",
            greenhouseResponse,
            HttpStatusCode.OK));

        var signal = new AtsCrossCheckSignal(client);
        var posting = new JobPosting
        {
            Source = JobSource.Greenhouse,
            CompanyName = "Acme Corp",
            JobTitle = "Senior Software Engineer",
            Location = "San Francisco, CA",
            Url = "https://boards.greenhouse.io/acme/jobs/12345"
        };

        var result = await signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.True(result.Score > 0.5, $"Expected high score but got {result.Score}");
        Assert.Contains("not found", result.Evidence[0]);
    }

    [Fact]
    public async Task EvaluateAsync_LeverJobFound_ReturnsLowScore()
    {
        var leverResponse = @"[
            { ""text"": ""Product Manager"", ""categories"": { ""location"": ""New York, NY"" } }
        ]";

        var client = MockClient((
            "https://api.lever.co/v0/postings/beta?mode=json",
            leverResponse,
            HttpStatusCode.OK));

        var signal = new AtsCrossCheckSignal(client);
        var posting = new JobPosting
        {
            Source = JobSource.Lever,
            CompanyName = "Beta Inc",
            JobTitle = "Product Manager",
            Location = "New York, NY",
            Url = "https://jobs.lever.co/beta/abc-def"
        };

        var result = await signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.True(result.Score < 0.5);
        Assert.Contains("Confirmed live", result.Evidence[0]);
    }

    [Fact]
    public async Task EvaluateAsync_Ats404_ReturnsNull()
    {
        var client = MockClient((
            "https://boards-api.greenhouse.io/v1/boards/acme/jobs",
            "",
            HttpStatusCode.NotFound));

        var signal = new AtsCrossCheckSignal(client);
        var posting = new JobPosting
        {
            Source = JobSource.Greenhouse,
            CompanyName = "Acme Corp",
            JobTitle = "Senior Software Engineer",
            Location = "San Francisco, CA",
            Url = "https://boards.greenhouse.io/acme/jobs/12345"
        };

        var result = await signal.EvaluateAsync(posting);

        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_UnsupportedUrl_ReturnsNull()
    {
        var client = new HttpClient(new MockHttpMessageHandler(Array.Empty<(string, string, HttpStatusCode)>())); // never called
        var signal = new AtsCrossCheckSignal(client);
        var posting = new JobPosting
        {
            Source = JobSource.Workday,
            CompanyName = "Delta Co",
            JobTitle = "Analyst",
            Location = "Chicago, IL",
            Url = "https://delta.wd101.com/job/123"
        };

        var result = await signal.EvaluateAsync(posting);

        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_InferredAtsNoMatch_ReturnsNull()
    {
        // When the ATS is inferred from company name (not the posting URL),
        // a no-match must NOT emit a high score — the guessed slug might
        // resolve to a different company's real board.
        var leverResponse = @"[
            { ""text"": ""Junior Designer"", ""categories"": { ""location"": ""Austin, TX"" } }
        ]";

        var client = MockClient((
            "https://api.lever.co/v0/postings/acme-corp?mode=json",
            leverResponse,
            HttpStatusCode.OK));

        var signal = new AtsCrossCheckSignal(client);
        var posting = new JobPosting
        {
            Source = JobSource.Indeed,
            CompanyName = "Acme Corp",
            JobTitle = "Senior Software Engineer",
            Location = "San Francisco, CA",
            Url = "https://www.indeed.com/viewjob?jk=abc123"
        };

        var result = await signal.EvaluateAsync(posting);

        Assert.Null(result); // precision-first: no high-confidence flag on a guess
    }

    [Fact]
    public async Task EvaluateAsync_InferredAtsMatchFound_ReturnsLowScore()
    {
        var leverResponse = @"[
            { ""text"": ""Senior Software Engineer"", ""categories"": { ""location"": ""San Francisco, CA"" } }
        ]";

        var client = MockClient((
            "https://api.lever.co/v0/postings/acme-corp?mode=json",
            leverResponse,
            HttpStatusCode.OK));

        var signal = new AtsCrossCheckSignal(client);
        var posting = new JobPosting
        {
            Source = JobSource.LinkedIn,
            CompanyName = "Acme Corp",
            JobTitle = "Senior Software Engineer",
            Location = "San Francisco, CA",
            Url = "https://www.linkedin.com/jobs/view/999888"
        };

        var result = await signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.True(result.Score < 0.5);
        Assert.Contains("Confirmed live", result.Evidence[0]);
    }

    // ── Mock handler ───────────────────────────────────────────────────

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (string Body, HttpStatusCode Status)> _responses;

        public MockHttpMessageHandler(IEnumerable<(string Url, string Response, HttpStatusCode Status)> endpoints)
        {
            _responses = endpoints.ToDictionary(e => e.Url, e => (e.Response, e.Status));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            if (_responses.TryGetValue(url, out var resp))
            {
                return Task.FromResult(new HttpResponseMessage(resp.Status)
                {
                    Content = new StringContent(resp.Body)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("")
            });
        }
    }
}
