using Envoy.Core.Data;
using Envoy.Core.Models;
using Envoy.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Envoy.Core.Tests;

public class JobEventRepositoryTests : IDisposable
{
    // Shared open connection keeps the in-memory database alive across the
    // short-lived contexts the repository creates per operation.
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
    private readonly JobEventRepository _repo;

    public JobEventRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _repo = new JobEventRepository(
            new TestDbContextFactory(_connection),
            Mock.Of<ILogger<JobEventRepository>>());
    }

    public void Dispose() => _connection.Dispose();

    private static JobEvent Event(JobEventType type, DateTime occurredAt) => new()
    {
        Type = type,
        OccurredAt = occurredAt,
        JobUrl = "https://boards.greenhouse.io/acme/jobs/123",
        JobTitle = "Engineer",
        Company = "Acme",
        PostingKey = "boards.greenhouse.io/acme/jobs/123",
        RiskBand = "High",
        RiskScore = 80
    };

    [Fact]
    public async Task AddAndGetAll_RoundTrips_NewestFirst()
    {
        var older = Event(JobEventType.Applied, new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc));
        var newer = Event(JobEventType.Declined, new DateTime(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc));
        await _repo.AddAsync(older);
        await _repo.AddAsync(newer);

        var all = await _repo.GetAllAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal(newer.Id, all[0].Id);
        Assert.Equal(older.Id, all[1].Id);
        Assert.Equal("High", all[0].RiskBand);
        Assert.Equal(80, all[0].RiskScore);
    }

    [Fact]
    public async Task CountByType_CountsOnlyMatchingEvents()
    {
        var now = DateTime.UtcNow;
        await _repo.AddAsync(Event(JobEventType.Applied, now));
        await _repo.AddAsync(Event(JobEventType.Applied, now));
        await _repo.AddAsync(Event(JobEventType.Declined, now));

        Assert.Equal(2, await _repo.CountByTypeAsync(JobEventType.Applied));
        Assert.Equal(1, await _repo.CountByTypeAsync(JobEventType.Declined));
        Assert.Equal(0, await _repo.CountByTypeAsync(JobEventType.Skipped));
    }
}
