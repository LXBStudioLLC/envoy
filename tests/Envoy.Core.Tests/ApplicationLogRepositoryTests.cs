using Envoy.Core.Data;
using Envoy.Core.Models;
using Envoy.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Envoy.Core.Tests;

public class ApplicationLogRepositoryTests : IDisposable
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
    private readonly ApplicationLogRepository _repo;

    public ApplicationLogRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _repo = new ApplicationLogRepository(
            new TestDbContextFactory(_connection),
            Mock.Of<ILogger<ApplicationLogRepository>>());
    }

    public void Dispose() => _connection.Dispose();

    private static ApplicationLog Log() => new()
    {
        JobUrl = "https://boards.greenhouse.io/acme/jobs/123",
        JobTitle = "Engineer",
        Company = "Acme",
        Status = ApplicationStatus.Completed,
        BeforeScreenshot = new byte[] { 1, 2, 3 },
        AfterScreenshot = new byte[] { 4, 5, 6 }
    };

    [Fact]
    public async Task SetOutcome_UpdatesOutcome_AndNeverTouchesScreenshots()
    {
        var log = Log();
        await _repo.AddAsync(log);

        await _repo.SetOutcomeAsync(log.Id, ResponseOutcome.Interview);

        var reloaded = await _repo.GetByIdAsync(log.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(ResponseOutcome.Interview, reloaded!.Outcome);
        Assert.Equal(new byte[] { 1, 2, 3 }, reloaded.BeforeScreenshot);
        Assert.Equal(new byte[] { 4, 5, 6 }, reloaded.AfterScreenshot);
    }

    [Fact]
    public async Task GetAllWithoutScreenshots_OmitsBlobs_ButKeepsTheRestOfTheRow()
    {
        var log = Log();
        await _repo.AddAsync(log);
        await _repo.SetOutcomeAsync(log.Id, ResponseOutcome.Rejected);

        var lite = (await _repo.GetAllWithoutScreenshotsAsync()).Single();

        Assert.Equal(log.Id, lite.Id);
        Assert.Equal("Acme", lite.Company);
        Assert.Equal(ApplicationStatus.Completed, lite.Status);
        Assert.Equal(ResponseOutcome.Rejected, lite.Outcome);
        Assert.Null(lite.BeforeScreenshot);
        Assert.Null(lite.AfterScreenshot);
    }
}
