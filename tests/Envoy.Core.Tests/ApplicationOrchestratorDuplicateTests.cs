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
/// Integration tests for the duplicate guard inside
/// <see cref="ApplicationOrchestrator.SubmitApplicationAsync"/> (APP-04).
/// Real repositories over in-memory SQLite, stubbed browser launcher: zero
/// network, and the blocked path must never even reach the browser.
/// </summary>
public sealed class ApplicationOrchestratorDuplicateTests : IDisposable
{
    private sealed class TestDbContextFactory : IDbContextFactory<EnvoyDbContext>
    {
        private readonly DbContextOptions<EnvoyDbContext> _options;

        public TestDbContextFactory(SqliteConnection connection)
        {
            _options = new DbContextOptionsBuilder<EnvoyDbContext>()
                .UseSqlite(connection)
                .Options;
            using var db = new EnvoyDbContext(_options);
            db.Database.EnsureCreated();
        }

        public EnvoyDbContext CreateDbContext() => new(_options);
    }

    private readonly SqliteConnection _connection;
    private readonly EnvoySettings _settings = new() { CaptureScreenshots = false };

    public ApplicationOrchestratorDuplicateTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private static GenericFormFiller GenericFixedFiller() =>
        new(new FakeEnumerator(), new FakeActuator(), new FormFieldMapper(
            new OllamaService(Mock.Of<ILLMProvider>()), new TestBank()), null);

    private sealed class FakeEnumerator : IFormFieldEnumerator
    {
        public Task<List<FormFieldInfo>> EnumerateAsync(CancellationToken ct = default)
            => Task.FromResult(new List<FormFieldInfo>());
    }

    private sealed class FakeActuator : IFormFieldActuator
    {
        public Task<bool> FillTextAsync(string selector, string value, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> SelectOptionAsync(string selector, string optionText, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> SetCheckedAsync(string selector, bool isChecked, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> UploadFileAsync(string selector, string filePath, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> ExistsAsync(string selector, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class TestBank : IAnswerBankRepository
    {
        public Task<List<AnswerBankEntry>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(new List<AnswerBankEntry>());
        public Task AddOrUpdateAsync(string question, string answer, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private ApplicationOrchestrator BuildOrchestrator(out ApplicationLogRepository logs,
        out TailoredProfileRepository tailored, out Mock<IBrowserLauncher> launcher)
    {
        var factory = new TestDbContextFactory(_connection);
        logs = new ApplicationLogRepository(factory, Mock.Of<ILogger<ApplicationLogRepository>>());
        tailored = new TailoredProfileRepository(factory, Mock.Of<ILogger<TailoredProfileRepository>>());
        launcher = new Mock<IBrowserLauncher>();

        var ollama = new OllamaService(Mock.Of<ILLMProvider>());
        return new ApplicationOrchestrator(
            new ResumeParserService(ollama, new SafetyService(Mock.Of<ILogger<SafetyService>>())),
            new TailoringEngine(ollama, new SafetyService(Mock.Of<ILogger<SafetyService>>()), factory, Mock.Of<ILogger<TailoringEngine>>()),
            new CdpBrowserService(new HumanizationService(), _settings, Mock.Of<ILogger<CdpBrowserService>>()),
            new TemplateEngine(Path.Combine(AppContext.BaseDirectory, "Templates")),
            GenericFixedFiller(),
            Mock.Of<IResumePdfGenerator>(),
            new ProfileRepository(factory, Mock.Of<ILogger<ProfileRepository>>()),
            tailored, logs,
            new JobEventRepository(factory, Mock.Of<ILogger<JobEventRepository>>()),
            launcher.Object,
            _settings,
            Mock.Of<ILogger<ApplicationOrchestrator>>());
    }

    private static TailoredProfile NewTailored(string url, string company, string title) => new()
    {
        MasterProfileId = Guid.NewGuid(),
        JobUrl = url,
        Company = company,
        JobTitle = title,
        JobDescriptionText = "Software engineer role. Design, build, ship. C# .NET Azure. Competitive salary offered.",
        TailoredData = new MasterProfile { Name = "Test User", Email = "test@example.com" },
        MatchScore = 50
    };

    private static CancellationToken Cts() => CancellationToken.None;

    [Fact]
    public async Task DuplicateBlocking_PriorCompleted_NeverReachesBrowser()
    {
        var orchestrator = BuildOrchestrator(out var logs, out var tailored, out var launcher);
        var url = "https://boards.greenhouse.io/acme/jobs/123?source=linkedin";

        var created = NewTailored(url, "Acme, Inc.", "Software Engineer");
        await tailored.AddAsync(created, Cts());
        await logs.AddAsync(new ApplicationLog
        {
            TailoredProfileId = created.Id,
            JobUrl = url,
            Company = created.Company,
            JobTitle = created.JobTitle,
            Status = ApplicationStatus.Completed,
            StartedAt = DateTime.UtcNow.AddMinutes(-1),
            CompletedAt = DateTime.UtcNow
        }, Cts());

        var launcherCalls = 0;
        launcher.Setup(l => l.IsRunningWithDebuggingAsync(It.IsAny<BrowserType>(), It.IsAny<int>()))
            .ReturnsAsync(true)
            .Callback(() => launcherCalls++);

        var result = await orchestrator.SubmitApplicationAsync(
            created.Id, ExecutionMode.Safe, _ => Task.FromResult(true),
            onDuplicateDetected: _ => Task.FromResult(false), ct: Cts());

        Assert.Equal(ApplicationStatus.DuplicateSkipped, result.Status);
        Assert.Contains("already completed", result.ErrorMessage);
        Assert.Contains(created.Company, result.ErrorMessage);
        Assert.Equal(0, launcherCalls); // blocked before any browser work
    }

    [Fact]
    public async Task DuplicateOverride_UserConsents_ProceedsPastTheGuard()
    {
        var orchestrator = BuildOrchestrator(out var logs, out var tailored, out var launcher);
        var url = "https://boards.greenhouse.io/acme/jobs/200";

        var created = NewTailored(url, "Acme, Inc.", "Software Engineer");
        await tailored.AddAsync(created, Cts());
        await logs.AddAsync(new ApplicationLog
        {
            TailoredProfileId = created.Id,
            JobUrl = url,
            Company = created.Company,
            JobTitle = created.JobTitle,
            Status = ApplicationStatus.Completed,
            StartedAt = DateTime.UtcNow.AddMinutes(-1),
            CompletedAt = DateTime.UtcNow
        }, Cts());

        // No browser available in this test: with the override granted, the
        // flow continues past the guard and stops at the browser launch step.
        launcher.Setup(l => l.IsRunningWithDebuggingAsync(It.IsAny<BrowserType>(), It.IsAny<int>())).ReturnsAsync(false);
        launcher.Setup(l => l.LaunchAsync(It.IsAny<BrowserType>(), It.IsAny<int>())).ReturnsAsync(false);

        var result = await orchestrator.SubmitApplicationAsync(
            created.Id, ExecutionMode.Safe, _ => Task.FromResult(true),
            onDuplicateDetected: _ => Task.FromResult(true), ct: Cts());

        Assert.Equal(ApplicationStatus.Failed, result.Status);
        Assert.NotEqual(ApplicationStatus.DuplicateSkipped, result.Status);
        Assert.Contains("launch", result.ErrorMessage ?? string.Empty);
    }

    [Fact]
    public async Task NoPriorHistory_ProceedsToBrowserPath()
    {
        var orchestrator = BuildOrchestrator(out var logs, out var tailored, out var launcher);
        var created = NewTailored("https://jobs.lever.co/acme/555", "Lever Co Inc", "Support Engineer");
        await tailored.AddAsync(created, Cts());

        launcher.Setup(l => l.IsRunningWithDebuggingAsync(It.IsAny<BrowserType>(), It.IsAny<int>())).ReturnsAsync(false);
        launcher.Setup(l => l.LaunchAsync(It.IsAny<BrowserType>(), It.IsAny<int>())).ReturnsAsync(false);

        var result = await orchestrator.SubmitApplicationAsync(
            created.Id, ExecutionMode.Safe, _ => Task.FromResult(true), ct: Cts());

        Assert.Equal(ApplicationStatus.Failed, result.Status);
        var stored = await logs.GetAllWithoutScreenshotsAsync(Cts());
        Assert.Single(stored); // exactly one log for this attempt — no duplicate row written
    }
}
