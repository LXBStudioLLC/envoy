using Envoy.Discovery.Sources;
using Envoy.GhostDetection.Models;
using Xunit;

namespace Envoy.Discovery.Tests;

public class AtsSourceTests
{
    [Fact]
    public async Task Greenhouse_ParsesAndDecodesEscapedHtml()
    {
        const string json = """
        { "jobs": [ {
            "id": 123,
            "title": "Senior Engineer",
            "updated_at": "2026-06-01T10:00:00-05:00",
            "location": { "name": "Remote" },
            "absolute_url": "https://boards.greenhouse.io/acme/jobs/123",
            "content": "&lt;p&gt;Build &amp;amp; ship&lt;/p&gt;"
        } ] }
        """;
        var source = new GreenhouseSource(StubHttpMessageHandler.Client(json));

        var jobs = await source.FetchBoardAsync("acme", companyName: null);

        var job = Assert.Single(jobs);
        Assert.Equal(JobSource.Greenhouse, job.Source);
        Assert.Equal("Senior Engineer", job.JobTitle);
        Assert.Equal("Acme", job.CompanyName);          // prettified from token
        Assert.Equal("Remote", job.Location);
        Assert.Equal("Build & ship", job.DescriptionText); // tags stripped, entities decoded
        Assert.Equal(2026, job.PostedAtUtc!.Value.Year);
    }

    [Fact]
    public async Task Lever_ParsesArrayAndUnixMillisDate()
    {
        const string json = """
        [ {
            "id": "abc",
            "text": "Data Scientist",
            "categories": { "location": "New York" },
            "descriptionPlain": "Do data science.",
            "hostedUrl": "https://jobs.lever.co/acme/abc",
            "createdAt": 1751614630001
        } ]
        """;
        var source = new LeverSource(StubHttpMessageHandler.Client(json));

        var job = Assert.Single(await source.FetchBoardAsync("acme", "Acme Corp"));
        Assert.Equal("Data Scientist", job.JobTitle);
        Assert.Equal("Acme Corp", job.CompanyName);     // explicit name wins over token
        Assert.Equal("New York", job.Location);
        Assert.Equal("Do data science.", job.DescriptionText);
        Assert.NotNull(job.PostedAtUtc);
    }

    [Fact]
    public async Task Ashby_ParsesPublishedAtAndPlainDescription()
    {
        const string json = """
        { "jobs": [ {
            "id": "x1",
            "title": "Product Manager",
            "location": "San Francisco",
            "descriptionPlain": "Lead products.",
            "jobUrl": "https://jobs.ashbyhq.com/acme/x1",
            "publishedAt": "2026-03-12T16:38:15.322+00:00"
        } ] }
        """;
        var source = new AshbySource(StubHttpMessageHandler.Client(json));

        var job = Assert.Single(await source.FetchBoardAsync("acme", null));
        Assert.Equal(JobSource.Ashby, job.Source);
        Assert.Equal("Product Manager", job.JobTitle);
        Assert.Equal("San Francisco", job.Location);
        Assert.Equal("Lead products.", job.DescriptionText);
        Assert.Equal(2026, job.PostedAtUtc!.Value.Year);
    }

    [Fact]
    public async Task Workable_UsesAccountNameAndMarksRemote()
    {
        const string json = """
        { "name": "Acme Inc", "jobs": [ {
            "title": "Designer",
            "shortcode": "SC1",
            "telecommuting": true,
            "city": "Austin", "state": "TX", "country": "United States",
            "url": "https://apply.workable.com/j/SC1",
            "description": "<p>Design things</p>",
            "published_on": "2026-05-01"
        } ] }
        """;
        var source = new WorkableSource(StubHttpMessageHandler.Client(json));

        var job = Assert.Single(await source.FetchBoardAsync("acme", null));
        Assert.Equal("Acme Inc", job.CompanyName);       // top-level account name
        Assert.Contains("Remote", job.Location);
        Assert.Contains("Austin", job.Location);
        Assert.Equal("Design things", job.DescriptionText);
    }

    [Fact]
    public async Task Recruitee_ParsesNonIsoUtcDate()
    {
        const string json = """
        { "offers": [ {
            "id": 7,
            "title": "Recruiter",
            "company_name": "Acme BV",
            "location": "Amsterdam",
            "careers_url": "https://acme.recruitee.com/o/recruiter",
            "description": "<p>Hire people</p>",
            "published_at": "2026-04-01 09:00:00 UTC"
        } ] }
        """;
        var source = new RecruiteeSource(StubHttpMessageHandler.Client(json));

        var job = Assert.Single(await source.FetchBoardAsync("acme", null));
        Assert.Equal("Acme BV", job.CompanyName);
        Assert.Equal("Hire people", job.DescriptionText);
        Assert.Equal(new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc), job.PostedAtUtc);
    }

    [Fact]
    public async Task Recruitee_InvalidSubdomain_ReturnsEmptyWithoutCall()
    {
        // A bad token must not be turned into a hostname / network call.
        var handler = new StubHttpMessageHandler("{}");
        var source = new RecruiteeSource(new HttpClient(handler));

        var jobs = await source.FetchBoardAsync("bad token!", null);

        Assert.Empty(jobs);
        Assert.Null(handler.LastRequest); // no request was made
    }
}
