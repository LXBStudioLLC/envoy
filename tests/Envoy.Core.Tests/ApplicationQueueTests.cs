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
    private readonly TestFactory _factory;
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
        _factory = new TestFactory(options);
        _repo = new ApplicationQueueRepository(_factory, Mock.Of<ILogger<ApplicationQueueRepository>>());
        _service = new ApplicationQueueService(_repo, _preparer, Mock.Of<ILogger<ApplicationQueueService>>());
        _service.QueueChanged += () => Interlocked.Increment(ref _changeEvents);
    }

    public void Dispose()
    {
        _service.Dispose();
        _connection.Dispose();
    }

    private static QueuedApplication Item(string key, string url, string company = "Acme") => new()
    {
        MasterProfileId = Profile,
        PostingKey = key,
        JobUrl = url,
        JobTitle = "Engineer",
        Company = company,
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

        // APP-11 audit fix: a blocked duplicate is a TRANSIENT verdict — it is
        // returned to the caller, not persisted. Rows enqueued on every click
        // of a blocked posting would otherwise accumulate in the panel.
        var rows = await _repo.GetAllAsync();
        Assert.Empty(rows);
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

    // ── APP-11: batch guardrails ─────────────────────────────────────────

    [Fact]
    public async Task PerCompanyThrottle_SecondActiveRefused()
    {
        _preparer.Handler = _ => Task.FromResult(Guid.NewGuid()); // keep rows active (AppReady)
        var (ok1, _) = await _service.EnqueueAsync(Item("k1", "https://x.com/1", "Acme"));
        var (ok2, reason2) = await _service.EnqueueAsync(Item("k2", "https://x.com/2", "Acme"));

        Assert.True(ok1);
        Assert.False(ok2);
        Assert.Contains("one active per company", reason2);

        // A different company sails through.
        var (ok3, _) = await _service.EnqueueAsync(Item("k3", "https://x.com/3", "Beta"));
        Assert.True(ok3);
    }

    [Fact]
    public async Task DailyCap_BoundsTheBatch()
    {
        _preparer.Handler = _ => Task.FromResult(Guid.NewGuid()); // keep rows active (AppReady)
        var capped = new ApplicationQueueService(_repo, _preparer, Mock.Of<ILogger<ApplicationQueueService>>(),
            () => new EnvoySettings { QueueDailyCap = 2 });

        var (ok1, _) = await capped.EnqueueAsync(Item("k1", "https://x.com/1", "Acme"));
        var (ok2, _) = await capped.EnqueueAsync(Item("k2", "https://x.com/2", "Beta"));
        var (ok3, reason3) = await capped.EnqueueAsync(Item("k3", "https://x.com/3", "Gamma"));

        Assert.True(ok1);
        Assert.True(ok2);
        Assert.False(ok3);
        Assert.Contains("Daily cap reached (2", reason3);
        capped.Dispose();
    }

    [Theory]
    [InlineData(2, null, null)]      // no window: any hour
    [InlineData(2, 22, 6)]           // overnight wrap: 02:00 inside 22→06
    [InlineData(23, 22, 6)]          // 23:00 inside
    [InlineData(5, 22, 6)]           // 05:00 inside (end exclusive)
    public void InWindow_HonorsTheDrainWindow(int nowHour, int? start, int? end)
    {
        var now = new DateTime(2026, 11, 5, nowHour, 0, 0, DateTimeKind.Utc);
        Assert.True(FullAutoPolicy.InWindow(start, end, now));
    }

    [Theory]
    [InlineData(12, 22, 6)]          // 12:00 outside 22→06
    [InlineData(6, 22, 6)]           // end exclusive
    [InlineData(12, null, 6)]        // half-set window = misconfiguration
    public void InWindow_OutsideOrMisconfigured_Denies(int nowHour, int? start, int? end)
    {
        var now = new DateTime(2026, 11, 5, nowHour, 0, 0, DateTimeKind.Utc);
        Assert.False(FullAutoPolicy.InWindow(start, end, now));
    }

    [Fact]
    public async Task OutsideWindow_RowStaysAppReady_PolicyGateDeniesAtNoon()
    {
        var submitCalls = 0;
        var submitter = new StubSubmitter((_, log) =>
        {
            Interlocked.Increment(ref submitCalls);
            log.VerifiedSubmission = true;
            return (log, null, false);
        });

        // The REAL policy gate: FullAutoEnabled + company-ATS + trusted host —
        // with a night window 22→06 UTC, drained at noon UTC: the window denies.
        var logRepo = new ApplicationLogRepository(_factory, Mock.Of<ILogger<ApplicationLogRepository>>());
        var settings = new EnvoySettings
        {
            FullAutoEnabled = true,
            FullAutoWindowStartHour = 22,
            FullAutoWindowEndHour = 6,
            FullAutoTrustedHosts = { "boards.greenhouse.io" }
        };
        var realGate = new QueueFullAutoGate(logRepo, () => settings);

        var auto = new ApplicationQueueService(_repo, _preparer, Mock.Of<ILogger<ApplicationQueueService>>(),
            () => settings, submitter, realGate);
        var tailored = Guid.NewGuid();
        _preparer.Handler = _ => Task.FromResult(tailored);

        var item = Item("k-w", "https://boards.greenhouse.io/acme/jobs/30");
        await auto.EnqueueAsync(item, autoStart: false);
        var stored = (await _repo.GetAllAsync()).Single();

        await auto.ProcessItemAsync(stored, new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc), CancellationToken.None);

        var rows = await _repo.GetAllAsync();
        Assert.Equal(QueueItemStatus.AppReady, rows.Single().Status);
        Assert.Equal(0, submitCalls);
    }

    private sealed class CountingGate(bool admits, Action onCall) : IFullAutoGate
    {
        public bool ShouldAutoSubmitNow(QueuedApplication item, DateTime nowUtc) { onCall(); return admits; }
    }

    [Fact]
    public async Task CapZero_DisablesTheCap()
    {
        var uncapped = new ApplicationQueueService(_repo, _preparer, Mock.Of<ILogger<ApplicationQueueService>>(),
            () => new EnvoySettings { QueueDailyCap = 0 });

        var enqueuedCount = 0;
        for (var i = 0; i < 4; i++)
        {
            var (ok, _) = await uncapped.EnqueueAsync(Item("k" + i, "https://x.com/" + i, "Co " + i), autoStart: false);
            if (ok) enqueuedCount++;
        }
        uncapped.Dispose();
        Assert.Equal(4, enqueuedCount);
    }

    // ── APP-12: Full-Auto drain ──────────────────────────────────────────

    [Fact]
    public async Task FullAutoGateAdmits_SubmitterRuns_RowSubmitted()
    {
        var submitCalls = 0;
        var submitter = new StubSubmitter((_, log) =>
        {
            Interlocked.Increment(ref submitCalls);
            log.VerifiedSubmission = true;
            return (log, null, false);
        });
        var gate = new StubGate(true);
        var tailored = Guid.NewGuid();
        _preparer.Handler = _ => Task.FromResult(tailored);
        var auto = new ApplicationQueueService(_repo, _preparer, Mock.Of<ILogger<ApplicationQueueService>>(),
            () => new EnvoySettings { FullAutoEnabled = true }, submitter, gate);

        await auto.EnqueueAsync(Item("k-fa", "https://boards.greenhouse.io/acme/jobs/9"));

        // the worker prepares (sets the tailored id) then auto-submits
        await WaitUntilAsync(
            async () => (await _repo.GetAllAsync()).Any(r => r.Status == QueueItemStatus.Submitted),
            TimeSpan.FromSeconds(8));
        Assert.Equal(1, Interlocked.CompareExchange(ref submitCalls, 0, 0));
    }

    [Fact]
    public async Task FullAutoGateDenies_RowStaysAppReady()
    {
        var submitter = new StubSubmitter((_, log) =>
        {
            log.VerifiedSubmission = true;
            return (log, null, false);
        });
        var gate = new StubGate(false);
        var tailored = Guid.NewGuid();
        _preparer.Handler = _ => Task.FromResult(tailored);
        var auto = new ApplicationQueueService(_repo, _preparer, Mock.Of<ILogger<ApplicationQueueService>>(),
            () => new EnvoySettings { FullAutoEnabled = true }, submitter, gate);

        await auto.EnqueueAsync(Item("k-sa", "https://boards.greenhouse.io/acme/jobs/21"));

        await WaitUntilAsync(
            async () => (await _repo.GetAllAsync()).Single().Status == QueueItemStatus.AppReady,
            TimeSpan.FromSeconds(8));

        // no submit notification fired
        Assert.DoesNotContain(await _repo.GetAllAsync(), r => r.Status == QueueItemStatus.Submitted);
    }

    [Fact]
    public async Task FullAuto_GenericFill_ParksAsNeedsUserSend_NotSubmitted()
    {
        // The generic fill path completes with NeedsUserSubmit: the queue row
        // must park for the user's hand — never inflate the submitted count.
        var submitter = new StubSubmitter((_, log) =>
        {
            log.Status = ApplicationStatus.NeedsUserSubmit;
            log.VerificationNote = "generic fill complete";
            return (log, null, false);
        });
        var gate = new StubGate(true);
        var tailored = Guid.NewGuid();
        _preparer.Handler = _ => Task.FromResult(tailored);
        var auto = new ApplicationQueueService(_repo, _preparer, Mock.Of<ILogger<ApplicationQueueService>>(),
            () => new EnvoySettings { FullAutoEnabled = true }, submitter, gate);

        await auto.EnqueueAsync(Item("k-gen", "https://boards.greenhouse.io/acme/jobs/42"));

        await WaitUntilAsync(
            async () => (await _repo.GetAllAsync()).Single().Status == QueueItemStatus.NeedsUserSend,
            TimeSpan.FromSeconds(8));

        var row = (await _repo.GetAllAsync()).Single();
        Assert.Contains("press the site's Submit", row.Note);
        Assert.DoesNotContain(await _repo.GetAllAsync(), r => r.Status == QueueItemStatus.Submitted);
    }

    [Fact]
    public async Task FullAuto_CaptchaAtSubmit_ParksAsNeedsCaptcha()
    {
        var submitter = new StubSubmitter((_, log) =>
        {
            log.Status = ApplicationStatus.RequiresCaptcha;
            return (log, "captcha hand-off", true);
        });
        var gate = new StubGate(true);
        var tailored = Guid.NewGuid();
        _preparer.Handler = _ => Task.FromResult(tailored);
        var auto = new ApplicationQueueService(_repo, _preparer, Mock.Of<ILogger<ApplicationQueueService>>(),
            () => new EnvoySettings { FullAutoEnabled = true }, submitter, gate);

        await auto.EnqueueAsync(Item("k-cap", "https://boards.greenhouse.io/acme/jobs/77"));

        await WaitUntilAsync(
            async () => (await _repo.GetAllAsync()).Single().Status == QueueItemStatus.NeedsCaptcha,
            TimeSpan.FromSeconds(8));

        var row = (await _repo.GetAllAsync()).Single();
        Assert.Contains("captcha", row.Note, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(await _repo.GetAllAsync(), r => r.Status == QueueItemStatus.Submitted);
    }

    private sealed class StubSubmitter(Func<QueuedApplication, ApplicationLog, (ApplicationLog?, string?, bool)> handler) : IApplicationSubmitter
    {
        public Task<(ApplicationLog? Log, string? Error, bool Captcha)> SubmitAsync(QueuedApplication item, CancellationToken ct)
        {
            var log = new ApplicationLog
            {
                Id = Guid.NewGuid(),
                JobUrl = item.JobUrl,
                Company = item.Company,
                JobTitle = item.JobTitle,
                Status = ApplicationStatus.Completed
            };
            var result = handler(item, log);
            return Task.FromResult(((ApplicationLog?)result.Item1, result.Item2, result.Item3));
        }
    }

    private sealed class StubGate : IFullAutoGate
    {
        private readonly bool _admits;
        public StubGate(bool admits) => _admits = admits;
        public bool ShouldAutoSubmitNow(QueuedApplication item, DateTime nowUtc) => _admits;
    }
}
