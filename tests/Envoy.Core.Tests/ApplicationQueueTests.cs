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
/// APP-07 queue store tests: dedupe-on-active-key, next-item ordering, and
/// honest note/status round-trips over in-memory SQLite.
/// </summary>
public sealed class ApplicationQueueRepositoryTests : IDisposable
{
    private sealed class TestFactory : IDbContextFactory<EnvoyDbContext>
    {
        private readonly DbContextOptions<EnvoyDbContext> _options;
        public TestFactory(DbContextOptions<EnvoyDbContext> options) => _options = options;
        public EnvoyDbContext CreateDbContext() => new(_options);
    }

    private readonly SqliteConnection _connection;
    private readonly ApplicationQueueRepository _repo;
    private static readonly Guid Profile = Guid.NewGuid();

    public ApplicationQueueRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<EnvoyDbContext>().UseSqlite(_connection).Options;
        using var db = new EnvoyDbContext(options);
        db.Database.EnsureCreated();
        _repo = new ApplicationQueueRepository(new TestFactory(options), Mock.Of<ILogger<ApplicationQueueRepository>>());
    }

    public void Dispose() => _connection.Dispose();

    private static QueuedApplication Item(string key, string url, string company = "Acme") => new()
    {
        MasterProfileId = Profile,
        PostingKey = key,
        JobUrl = url,
        JobTitle = "Engineer",
        Company = company,
        DescriptionText = new string('x', 200)
    };

    [Fact]
    public async Task Add_ReturnsTrueOnce_PerActiveKey()
    {
        Assert.True(await _repo.AddAsync(Item("k1", "https://x.com/1")));
        // Same key still active → refused.
        Assert.False(await _repo.AddAsync(Item("k1", "https://x.com/1")));

        // Flip the row past active: the key frees up.
        var all = await _repo.GetAllAsync();
        var first = all.Single();
        first.Status = QueueItemStatus.Failed;
        await _repo.UpdateAsync(first);

        Assert.True(await _repo.AddAsync(Item("k1", "https://x.com/1")));
    }

    [Fact]
    public async Task GetNextQueued_FifoWithinProfile_OnlyQueued()
    {
        await _repo.AddAsync(Item("k-old", "https://x.com/old"));
        await Task.Delay(20); // distinct CreatedAt
        await _repo.AddAsync(Item("k-new", "https://x.com/new"));

        // A Preparing row is not handed out again.
        var preparing = Item("k-p", "https://x.com/p");
        preparing.Status = QueueItemStatus.Preparing;
        await _repo.AddAsync(preparing);

        var next = await _repo.GetNextQueuedAsync(Profile);
        Assert.NotNull(next);
        Assert.Equal("k-old", next!.PostingKey);
    }

    [Fact]
    public async Task Update_StatusesAndNotesRoundTrip()
    {
        var item = Item("k", "https://x.com/k");
        await _repo.AddAsync(item);

        var stored = (await _repo.GetAllAsync()).Single();
        stored.Status = QueueItemStatus.AppReady;
        stored.Note = "ready — 3 of 3 required fields";
        stored.TailoredProfileId = Guid.NewGuid();
        await _repo.UpdateAsync(stored);

        var after = (await _repo.GetAllAsync()).Single();
        Assert.Equal(QueueItemStatus.AppReady, after.Status);
        Assert.Contains("3 of 3", after.Note);
        Assert.NotNull(after.TailoredProfileId);
    }

    [Fact]
    public async Task HasActiveKey_ReflectsStatuses()
    {
        await _repo.AddAsync(Item("k-a", "https://x.com/a"));
        Assert.True(await _repo.HasActiveKeyAsync("k-a"));

        var all = await _repo.GetAllAsync();
        var row = all.Single();
        row.Status = QueueItemStatus.Submitted;
        await _repo.UpdateAsync(row);
        Assert.False(await _repo.HasActiveKeyAsync("k-a"));
    }

    [Fact]
    public async Task Remove_Deletes()
    {
        var item = Item("k", "https://x.com/k");
        await _repo.AddAsync(item);
        var stored = (await _repo.GetAllAsync()).Single();
        await _repo.RemoveAsync(stored.Id);
        Assert.Empty(await _repo.GetAllAsync());
    }
}

/// <summary>
/// APP-07 worker tests with a stubbed preparer: full lifecycle assertions,
/// offline and deterministic (bounded polling instead of sleeps).
/// </summary>
public sealed class ApplicationQueueServiceTests : IDisposable
{
    private sealed class TestFactory : IDbContextFactory<EnvoyDbContext>
    {
        private readonly DbContextOptions<EnvoyDbContext> _options;
        public TestFactory(DbContextOptions<EnvoyDbContext> options) => _options = options;
        public EnvoyDbContext CreateDbContext() => new(_options);
    }

    private sealed class StubPreparer : IApplicationPreparer
    {
        public Func<QueuedApplication, Task<Guid>>? Handler { get; set; }
        public Task<Guid> PrepareAsync(QueuedApplication item, CancellationToken ct) => Handler!(item);
    }

    private readonly SqliteConnection _connection;
    private readonly ApplicationQueueRepository _repo;
    private readonly StubPreparer _preparer = new();
    private readonly ApplicationQueueService _service;
    private static readonly Guid Profile = Guid.NewGuid();
    private int _changeEvents;

    public ApplicationQueueServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<EnvoyDbContext>().UseSqlite(_connection).Options;
        using var db = new EnvoyDbContext(options);
        db.Database.EnsureCreated();
        _repo = new ApplicationQueueRepository(new TestFactory(options), Mock.Of<ILogger<ApplicationQueueRepository>>());
        _service = new ApplicationQueueService(_repo, _preparer, Mock.Of<ILogger<ApplicationQueueService>>());
        _service.QueueChanged += () => Interlocked.Increment(ref _changeEvents);
    }

    public void Dispose()
    {
        _service.Dispose();
        _connection.Dispose();
    }

    private QueuedApplication Item(string key, string url) => new()
    {
        MasterProfileId = Profile,
        PostingKey = key,
        JobUrl = url,
        JobTitle = "Engineer",
        Company = "Acme",
        DescriptionText = new string('x', 200)
    };

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("condition not met in time");
    }

    [Fact]
    public async Task Enqueue_AutoStartsWorker_ItemsReachAppReady()
    {
        var tailoredId = Guid.NewGuid();
        _preparer.Handler = _ => Task.FromResult(tailoredId);

        var (enqueued, _) = await _service.EnqueueAsync(Item("k1", "https://x.com/1"));

        Assert.True(enqueued);
        Assert.True(_service.IsRunning);
        var id2ref = (await _repo.GetAllAsync()).Single();
        await WaitUntilAsync(
            async () => (await _repo.GetAllAsync()).Single().Status == QueueItemStatus.AppReady,
            TimeSpan.FromSeconds(6));

        var row = (await _repo.GetAllAsync()).Single();
        Assert.Equal(QueueItemStatus.AppReady, row.Status);
        Assert.Equal(tailoredId, row.TailoredProfileId);
        Assert.True(_changeEvents > 0);
    }

    [Fact]
    public async Task PreparerThrow_ItemFailsWithNote()
    {
        _preparer.Handler = _ => throw new InvalidOperationException("LLM exploded");

        await _service.EnqueueAsync(Item("k1", "https://x.com/1"));

        await WaitUntilAsync(
            async () => (await _repo.GetAllAsync()).Single().Status == QueueItemStatus.Failed,
            TimeSpan.FromSeconds(6));

        var row = (await _repo.GetAllAsync()).Single();
        Assert.Contains("LLM exploded", row.Note);
    }

    [Fact]
    public async Task DuplicateCheckBlocksAtEnqueue()
    {
        var (enqueued, reason) = await _service.EnqueueAsync(
            Item("k1", "https://x.com/already"),
            duplicateCheck: _ => Task.FromResult<string?>("You already completed an application for this posting on 2026-11-01."));

        Assert.False(enqueued);
        Assert.Contains("already completed", reason);

        var rows = await _repo.GetAllAsync();
        Assert.Single(rows);
        Assert.Equal(QueueItemStatus.BlockedDuplicate, rows[0].Status);
    }

    [Fact]
    public async Task ShortDescription_RefusedAtEnqueue()
    {
        var item = Item("k1", "https://x.com/short");
        item.DescriptionText = "too short";

        var (enqueued, reason) = await _service.EnqueueAsync(item, autoStart: false);

        Assert.False(enqueued);
        Assert.Contains("too short", reason);
    }

    [Fact]
    public async Task EnsureStartedTwice_NoDoubleProcessing()
    {
        var calls = 0;
        _preparer.Handler = _ => { Interlocked.Increment(ref calls); return Task.FromResult(Guid.NewGuid()); };

        _service.EnsureStarted();
        _service.EnsureStarted();

        await _service.EnqueueAsync(Item("k1", "https://x.com/1"), autoStart: false);

        await WaitUntilAsync(
            async () => (await _repo.GetAllAsync()).Single().Status == QueueItemStatus.AppReady,
            TimeSpan.FromSeconds(6));

        // Let the worker settle; a second worker would have processed the row twice.
        await Task.Delay(700);
        Assert.Equal(1, Interlocked.CompareExchange(ref calls, 0, 0));
    }
}
