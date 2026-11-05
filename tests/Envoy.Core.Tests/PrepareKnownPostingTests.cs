using Envoy.Core.Configuration;
using Envoy.Core.Data;
using Envoy.Core.Models;
using Envoy.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Envoy.Core.Tests;

/// <summary>
/// APP-02 fast path: preparing with a KnownPosting (description already
/// carried from discovery) must tailor WITHOUT touching the browser at all.
/// Offline: in-memory SQLite, stubbed LLM returning the profile verbatim,
/// browser launcher reporting "not running" — if the fast path wrongly
/// scraped, the launcher would be hit and the app would try to launch.
/// </summary>
public sealed class PrepareKnownPostingTests : IDisposable
{
    private sealed class EchoProvider : Services.ILLMProvider
    {
        public string ProviderId => "echo";
        public string DisplayName => "Echo";
        public string Description => "returns the profile unchanged";
        public bool RequiresApiKey => false;
        public bool IsLocal => true;
        public Task<bool> IsAvailableAsync() => Task.FromResult(true);
        public Task<List<LLMModelInfo>> ListModelsAsync() => Task.FromResult(new List<LLMModelInfo>());

        public Task<string> CompleteAsync(string prompt, string systemPrompt, string? modelId, CancellationToken ct = default)
            => Task.FromResult("not-json-on-purpose");

        public Task<T?> CompleteJsonAsync<T>(string prompt, string systemPrompt, string? modelId, CancellationToken ct = default)
            => Task.FromResult(default(T)); // fallback path: original profile, safety-flagged
    }

    private sealed class TestFactory : IDbContextFactory<EnvoyDbContext>
    {
        private readonly DbContextOptions<EnvoyDbContext> _options;
        public TestFactory(DbContextOptions<EnvoyDbContext> options) => _options = options;
        public EnvoyDbContext CreateDbContext() => new(_options);
    }

    private readonly SqliteConnection _connection;
    private readonly TestFactory _factory;
    private readonly Mock<IBrowserLauncher> _launcher = new();

    public PrepareKnownPostingTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<EnvoyDbContext>().UseSqlite(_connection).Options;
        _factory = new TestFactory(options);
        using var db = new EnvoyDbContext(options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private ApplicationOrchestrator BuildOrchestrator()
    {
        _launcher.Setup(l => l.IsRunningWithDebuggingAsync(It.IsAny<BrowserType>(), It.IsAny<int>()))
            .ReturnsAsync(false);
        _launcher.Setup(l => l.LaunchAsync(It.IsAny<BrowserType>(), It.IsAny<int>()))
            .ReturnsAsync(false);

        var ollama = new OllamaService(new EchoProvider());
        var settings = new EnvoySettings { CaptureScreenshots = false };

        return new ApplicationOrchestrator(
            new ResumeParserService(ollama, new SafetyService(Mock.Of<ILogger<SafetyService>>())),
            new TailoringEngine(ollama, new SafetyService(Mock.Of<ILogger<SafetyService>>()), _factory, Mock.Of<ILogger<TailoringEngine>>(), settings),
            new CdpBrowserService(new HumanizationService(), settings, Mock.Of<ILogger<CdpBrowserService>>()),
            new TemplateEngine(Path.Combine(AppContext.BaseDirectory, "Templates")),
            Mock.Of<IResumePdfGenerator>(),
            new ProfileRepository(_factory, Mock.Of<ILogger<ProfileRepository>>()),
            new TailoredProfileRepository(_factory, Mock.Of<ILogger<TailoredProfileRepository>>()),
            new ApplicationLogRepository(_factory, Mock.Of<ILogger<ApplicationLogRepository>>()),
            new JobEventRepository(_factory, Mock.Of<ILogger<JobEventRepository>>()),
            _launcher.Object,
            settings,
            Mock.Of<ILogger<ApplicationOrchestrator>>());
    }

    [Fact]
    public async Task KnownPosting_TailorsWithoutTouchingTheBrowser()
    {
        var orchestrator = BuildOrchestrator();
        var profile = new MasterProfile
        {
            Name = "Test User",
            Email = "test@example.com",
            Skills = new List<string> { "C#", "x" },
            Summary = "Engineer."
        };
        using (var db = _factory.CreateDbContext())
        {
            db.MasterProfiles.Add(profile);
            await db.SaveChangesAsync();
        }

        // The launcher is stubbed to FAIL both checks — a scrape attempt here
        // would surface as "Could not launch browser".
        var known = new KnownPosting(
            "https://boards.greenhouse.io/acme/jobs/777",
            "Acme, Inc.",
            "Software Engineer",
            "We are seeking a software engineer with strong distributed systems experience. " +
            "Design, develop and ship cloud services. Requirements: C#, .NET, 5+ years. Competitive salary.");

        var prepared = await orchestrator.PrepareApplicationAsync(profile.Id, known.JobUrl, known);

        Assert.Equal(known.Company, prepared.Tailored.Company);   // hint applied even via fallback
        Assert.Equal(known.JobTitle, prepared.Tailored.JobTitle);
        Assert.Equal(known.DescriptionText, prepared.JobDescription);
        Assert.False(prepared.Tailored.SafetyResult.Passed); // echo-fallback is safety-flagged as designed
        _launcher.Verify(l => l.LaunchAsync(It.IsAny<BrowserType>(), It.IsAny<int>()), Times.Never);
        _launcher.Verify(l => l.IsRunningWithDebuggingAsync(It.IsAny<BrowserType>(), It.IsAny<int>()), Times.Never);
    }
}
