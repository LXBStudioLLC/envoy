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

    private static JobEvent Event(JobEventType type, DateTime occurredAt, string postingKey = "boards.greenhouse.io/acme/jobs/123") => new()
    {
        Type = type,
        OccurredAt = occurredAt,
        JobUrl = "https://boards.greenhouse.io/acme/jobs/123",
        JobTitle = "Engineer",
        Company = "Acme",
        PostingKey = postingKey,
        RiskBand = "High",
        RiskScore = 80
    };

    private static JobEvent Sighting(string postingKey, DateTime? occurredAt = null) =>
        Event(JobEventType.Sighted, occurredAt ?? DateTime.UtcNow, postingKey);

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

    [Fact]
    public async Task RecordSightings_InsertsFresh_SkipsSameDayDuplicates()
    {
        var first = await _repo.RecordSightingsAsync(new[]
        {
            Sighting("key-a"),
            Sighting("key-b"),
            Sighting("key-a")   // duplicate inside the same batch
        });
        Assert.Equal(2, first);

        var second = await _repo.RecordSightingsAsync(new[]
        {
            Sighting("key-a"),  // already sighted today
            Sighting("key-c")
        });
        Assert.Equal(1, second);

        Assert.Equal(3, await _repo.CountByTypeAsync(JobEventType.Sighted));
    }

    [Fact]
    public async Task RecordSightings_SamePostingOnALaterDay_RecordsAgain()
    {
        await _repo.AddAsync(Sighting("key-a", DateTime.UtcNow.AddDays(-1)));

        var inserted = await _repo.RecordSightingsAsync(new[] { Sighting("key-a") });

        Assert.Equal(1, inserted);
        Assert.Equal(2, await _repo.CountByTypeAsync(JobEventType.Sighted));
    }

    [Fact]
    public async Task RecordSightings_IgnoresNonSightedEvents()
    {
        var inserted = await _repo.RecordSightingsAsync(new[]
        {
            Event(JobEventType.Skipped, DateTime.UtcNow),
            Event(JobEventType.Viewed, DateTime.UtcNow)
        });

        Assert.Equal(0, inserted);
        Assert.Empty(await _repo.GetAllAsync());
    }
}
