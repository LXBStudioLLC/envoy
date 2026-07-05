using Envoy.Discovery;
using Envoy.Discovery.Models;
using Envoy.Discovery.Sources;
using Envoy.GhostDetection.Models;
using Envoy.GhostDetection.Signals;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Envoy.Discovery.Tests;

public class JobDiscoveryServiceTests
{
    private sealed class FakeAtsSource : IAtsBoardSource
    {
        private readonly IReadOnlyList<JobPosting> _jobs;
        private readonly Exception? _throw;
        public FakeAtsSource(JobSource ats, IReadOnlyList<JobPosting>? jobs = null, Exception? toThrow = null)
        {
            Ats = ats;
            _jobs = jobs ?? Array.Empty<JobPosting>();
            _throw = toThrow;
        }
        public JobSource Ats { get; }
        public Task<IReadOnlyList<JobPosting>> FetchBoardAsync(string token, string? companyName, CancellationToken ct = default)
            => _throw != null ? throw _throw : Task.FromResult(_jobs);
    }

    private sealed class FakeWebSearch : IWebSearchSource
    {
        public string Name => "Fake";
        public Task<IReadOnlyList<JobPosting>> SearchAsync(string apiKey, string query, int count, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<JobPosting>>(Array.Empty<JobPosting>());
    }

    private static JobPosting Job(string title, string company, string location, string url) =>
        new() { JobTitle = title, CompanyName = company, Location = location, Url = url, DescriptionText = title };

    // A description long enough (>=400 chars, >=60 tokens) for DuplicateJdSignal to compare.
    private const string LongJd =
        "We are seeking a senior backend engineer to design and operate large-scale distributed systems. " +
        "You will build resilient services, own reliability, mentor engineers, and collaborate across teams. " +
        "Requirements include deep experience with cloud infrastructure, databases, message queues, observability, " +
        "and a strong track record shipping production software. We value ownership, curiosity, and pragmatism. " +
        "Responsibilities span architecture, code review, incident response, capacity planning, and performance tuning. " +
        "This role offers competitive compensation, remote flexibility, and meaningful equity in a growing company.";

    private static JobPosting JobWithDesc(string title, string company, string url, string description) =>
        new() { JobTitle = title, CompanyName = company, Location = "Remote", Url = url, DescriptionText = description };

    private static JobDiscoveryService Service(params IAtsBoardSource[] sources) =>
        new(sources, new FakeWebSearch(), NullLogger<JobDiscoveryService>.Instance);

    [Fact]
    public async Task SearchBoards_FiltersByKeyword()
    {
        var src = new FakeAtsSource(JobSource.Greenhouse, new[]
        {
            Job("Rust Engineer", "Acme", "Remote", "https://x/1"),
            Job("Marketing Lead", "Acme", "Remote", "https://x/2"),
        });
        var service = Service(src);

        var result = await service.SearchBoardsAsync(
            new[] { new AtsBoardRef { Ats = JobSource.Greenhouse, Token = "acme" } },
            new DiscoveryQuery { Keywords = "rust" });

        var job = Assert.Single(result.Jobs);
        Assert.Equal("Rust Engineer", job.JobTitle);
        Assert.Equal(2, result.TotalBeforeFilter);
    }

    [Fact]
    public async Task SearchBoards_DedupesByUrl()
    {
        var a = new FakeAtsSource(JobSource.Greenhouse, new[] { Job("Engineer", "Acme", "Remote", "https://x/1") });
        var b = new FakeAtsSource(JobSource.Lever, new[] { Job("Engineer", "Acme", "Remote", "https://x/1") });
        var service = Service(a, b);

        var result = await service.SearchBoardsAsync(new[]
        {
            new AtsBoardRef { Ats = JobSource.Greenhouse, Token = "a" },
            new AtsBoardRef { Ats = JobSource.Lever, Token = "b" },
        }, new DiscoveryQuery());

        Assert.Single(result.Jobs);
    }

    [Fact]
    public async Task SearchBoards_CapturesPerBoardError()
    {
        var ok = new FakeAtsSource(JobSource.Greenhouse, new[] { Job("Engineer", "Acme", "Remote", "https://x/1") });
        var bad = new FakeAtsSource(JobSource.Lever, toThrow: new HttpRequestException("boom"));
        var service = Service(ok, bad);

        var result = await service.SearchBoardsAsync(new[]
        {
            new AtsBoardRef { Ats = JobSource.Greenhouse, Token = "a" },
            new AtsBoardRef { Ats = JobSource.Lever, Token = "b" },
        }, new DiscoveryQuery());

        Assert.Single(result.Jobs);                       // the good board still returns
        var error = Assert.Single(result.Errors);
        Assert.Contains("boom", error);
    }

    [Fact]
    public async Task WebSearch_NoApiKey_ReturnsError()
    {
        var service = Service();
        var result = await service.WebSearchAsync("", "rust", new DiscoveryQuery());

        Assert.Empty(result.Jobs);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void DefaultBoards_AreLoaded()
    {
        var service = Service();
        Assert.NotEmpty(service.DefaultBoards);
    }

    [Fact]
    public async Task SearchBoards_WiresDuplicateJdCorpus_SoDuplicateSignalFires()
    {
        var src = new FakeAtsSource(JobSource.Greenhouse, new[]
        {
            JobWithDesc("Backend Engineer", "Acme", "https://x/1", LongJd),
            JobWithDesc("Backend Engineer", "Globex", "https://x/2", LongJd),
        });
        var service = Service(src);

        var result = await service.SearchBoardsAsync(
            new[] { new AtsBoardRef { Ats = JobSource.Greenhouse, Token = "acme" } },
            new DiscoveryQuery { MaxResults = 25 });

        Assert.Equal(2, result.Jobs.Count);
        // Every posting now carries the same-batch corpus that no runtime code populated before.
        Assert.All(result.Jobs, j => Assert.True(j.Extra.ContainsKey("dupcheck.corpus")));

        // The previously-inert signal now actually fires on a cross-company near-duplicate.
        var signal = new DuplicateJdSignal();
        var acme = result.Jobs.First(j => j.CompanyName == "Acme");
        var outcome = await signal.EvaluateAsync(acme);
        Assert.NotNull(outcome);
        Assert.Equal("Duplicate JD", outcome!.SignalName);
    }

    [Fact]
    public async Task SearchBoards_SinglePosting_GetsNoDuplicateCorpus()
    {
        var src = new FakeAtsSource(JobSource.Greenhouse, new[]
        {
            JobWithDesc("Backend Engineer", "Acme", "https://x/1", LongJd),
        });
        var service = Service(src);

        var result = await service.SearchBoardsAsync(
            new[] { new AtsBoardRef { Ats = JobSource.Greenhouse, Token = "acme" } },
            new DiscoveryQuery { MaxResults = 25 });

        var job = Assert.Single(result.Jobs);
        Assert.False(job.Extra.ContainsKey("dupcheck.corpus"));
    }
}
