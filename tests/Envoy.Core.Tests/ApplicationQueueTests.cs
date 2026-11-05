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

    [Fact]
    public async Task GetNextQueuedAnyProfile_FifoAcrossProfiles_IgnoresProfileAndPreparing()
    {
        var profileB = Guid.NewGuid();

        var rowB = Item("k-b1", "https://x.com/b1");
        rowB.MasterProfileId = profileB;
        await _repo.AddAsync(rowB);
        await Task.Delay(20); // distinct CreatedAt

        var rowA = Item("k-a1", "https://x.com/a1"); // different profile, newer
        await _repo.AddAsync(rowA);
        await Task.Delay(20);

        var preparing = Item("k-p", "https://x.com/p");
        preparing.Status = QueueItemStatus.Preparing;
        await _repo.AddAsync(preparing);

        // Oldest-first across ALL profiles, not per-profile: profileA's row
        // (newer) must not jump ahead of profileB's (older), and Preparing
        // rows are never handed out again.
        var next = await _repo.GetNextQueuedAnyProfileAsync();
        Assert.Equal("k-b1", next!.PostingKey);
        Assert.Equal(profileB, next.MasterProfileId);
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

    [Fact]
    public async Task WorkerDrainsRowsAcrossProfiles_OldestFirst()
    {
        // Row for profile A enqueued first, then a row for a DIFFERENT profile.
        // The old single-profile worker processed only the first row's profile;
        // now both rows drain, oldest first, each tailored for its own profile.
        var preparedOrder = new System.Collections.Concurrent.ConcurrentQueue<string>();
        _preparer.Handler = item =>
        {
            preparedOrder.Enqueue(item.PostingKey);
            return Task.FromResult(Guid.NewGuid());
        };

        var a = Item("k-prof-a", "https://boards.greenhouse.io/acme/jobs/101", "Acme");
        await _service.EnqueueAsync(a);
        await Task.Delay(20); // distinct CreatedAt — B is strictly newer
        var b = Item("k-prof-b", "https://boards.greenhouse.io/lumon/jobs/202", "Lumon");
        b.MasterProfileId = Guid.NewGuid();
        await _service.EnqueueAsync(b);

        await WaitUntilAsync(
            async () => (await _repo.GetAllAsync()).Count(r => r.Status == QueueItemStatus.AppReady) == 2,
            TimeSpan.FromSeconds(10));

        // B's row was not starved by A's profile, and order is oldest-first.
        var drained = preparedOrder.ToList();
        Assert.Equal("k-prof-a", drained[0]);
        Assert.Equal("k-prof-b", drained[1]);
    }

    [Fact]
    public async Task LegacyUnstampedRow_BindsToStampedProfileAndRuns()
    {
        // Rows persisted before per-row stamping exist on upgraded machines.
        // They still run: the worker binds them to the first stamped row's
        // profile (the old single-profile resolution) instead of starving.
        var legacy = Item("k-legacy", "https://boards.greenhouse.io/acme/jobs/301", "Acme");
        legacy.MasterProfileId = Guid.Empty;
        await _repo.AddAsync(legacy);
        await Task.Delay(20);

        _preparer.Handler = _ => Task.FromResult(Guid.NewGuid());
        await _service.EnqueueAsync(Item("k-stamped", "https://boards.greenhouse.io/lumon/jobs/302", "Lumon"));

        await WaitUntilAsync(
            async () => (await _repo.GetAllAsync()).Count(r => r.Status == QueueItemStatus.AppReady) >= 2,
            TimeSpan.FromSeconds(10));

        var all = await _repo.GetAllAsync();
        Assert.DoesNotContain(all, r => r.Status == QueueItemStatus.Queued);
        Assert.DoesNotContain(all, r => r.Status == QueueItemStatus.Failed);
    }

    // ── #92: the Night-Owl drain ─────────────────────────────────────────

    [Fact]
    public async Task NightOwl_ParkedRowsDrainOnlyWhenTheWindowOpens()
    {
        // The REAL policy gate with a UTC window 22→06, and a REAL submitter.
        // No worker: autoStart=false keeps the queue dormant so the drain's
        // contract is testable with an injected "now" — no polling, no flakes.
        var settings = new EnvoySettings
        {
            FullAutoEnabled = true,
            FullAutoWindowStartHour = 22,
            FullAutoWindowEndHour = 6,
            FullAutoTrustedHosts = { "boards.greenhouse.io" }
        };
        var gate = new QueueFullAutoGate(
            new ApplicationLogRepository(_factory, Mock.Of<ILogger<ApplicationLogRepository>>()), () => settings);
        var submitCalls = 0;
        var submitter = new StubSubmitter((_, log) =>
        {
            Interlocked.Increment(ref submitCalls);
            log.VerifiedSubmission = true;
            return (log, null, false);
        });
        var auto = new ApplicationQueueService(_repo, _preparer, Mock.Of<ILogger<ApplicationQueueService>>(),
            () => settings, submitter, gate);

        var preparedCalls = 0;
        var tailored = Guid.NewGuid();
        _preparer.Handler = _ =>
        {
            Interlocked.Increment(ref preparedCalls);
            return Task.FromResult(tailored);
        };

        await auto.EnqueueAsync(Item("k-no", "https://boards.greenhouse.io/acme/jobs/901", "Acme"), autoStart: false);
        var item = (await _repo.GetAllAsync()).Single();
        var noon = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

        // Prepare outside the window: the drain consult at noon denies, so the
        // row parks AppReady (the old behavior — with the FIX: nothing is lost).
        await auto.ProcessItemAsync(item, noon, CancellationToken.None);
        Assert.Equal(1, Interlocked.CompareExchange(ref preparedCalls, 0, 0));

        // Noon drain: the window denies — the row stays parked, nothing submits.
        Assert.Null(await auto.DrainDueAsync(noon, CancellationToken.None));
        Assert.Equal(QueueItemStatus.AppReady, (await _repo.GetAllAsync()).Single().Status);
        Assert.Equal(0, Interlocked.CompareExchange(ref submitCalls, 0, 0));

        // The window opens: the drain submits the parked row WITHOUT re-preparing.
        var drained = await auto.DrainDueAsync(new DateTime(2026, 11, 5, 23, 0, 0, DateTimeKind.Utc), CancellationToken.None);
        Assert.NotNull(drained);
        var row = (await _repo.GetAllAsync()).Single();
        Assert.Equal(QueueItemStatus.Submitted, row.Status);
        Assert.Contains("verified", row.Note);
        Assert.Equal(1, Interlocked.CompareExchange(ref submitCalls, 0, 0));
        Assert.Equal(1, Interlocked.CompareExchange(ref preparedCalls, 0, 0)); // never re-prepared
    }

    [Fact]
    public void HostOf_ParsesBareHostTrustEntries()
    {
        // The settings example writes trust entries as bare hosts; a regression
        // that required a URL scheme made explicit trust lists silently dead
        // (diagnosed while wiring the Night-Owl drain).
        Assert.Equal("boards.greenhouse.io", FullAutoPolicy.HostOf("boards.greenhouse.io"));
        Assert.Equal("boards.greenhouse.io", FullAutoPolicy.HostOf("HTTPS://BOARDS.GREENHOUSE.IO/jobs/1"));
        Assert.Equal("boards.greenhouse.io", FullAutoPolicy.HostOf("www.boards.greenhouse.io"));
        Assert.Equal("", FullAutoPolicy.HostOf("not a url at all"));
        Assert.Equal("", FullAutoPolicy.HostOf(null));
    }

    [Fact]
    public async Task NightOwl_ManualFlowHoldsTheBrowser_DrainStandsDown()
    {
        // The human goes first: while a manual fill/tailor drives the shared
        // browser (flag set by the Apply view), the drain never submits — the
        // row stays parked, nothing moves; clearing the flag drains normally.
        var settings = new EnvoySettings
        {
            FullAutoEnabled = true,
            FullAutoTrustedHosts = { "boards.greenhouse.io" }
        };
        var gate = new QueueFullAutoGate(
            new ApplicationLogRepository(_factory, Mock.Of<ILogger<ApplicationLogRepository>>()), () => settings);
        var submitCalls = 0;
        var submitter = new StubSubmitter((_, log) =>
        {
            Interlocked.Increment(ref submitCalls);
            log.VerifiedSubmission = true;
            return (log, null, false);
        });
        var auto = new ApplicationQueueService(_repo, _preparer, Mock.Of<ILogger<ApplicationQueueService>>(),
            () => settings, submitter, gate);

        _preparer.Handler = _ => Task.FromResult(Guid.NewGuid());
        await auto.EnqueueAsync(Item("k-stall", "https://boards.greenhouse.io/acme/jobs/903", "Acme"), autoStart: false);
        var item = (await _repo.GetAllAsync()).Single();

        // The flag is set BEFORE the prepare pass too: even the submit consult
        // at the end of ProcessItemAsync stands down when a manual flow runs.
        auto.ManualBrowserFlowActive = true;
        var now = new DateTime(2026, 11, 5, 23, 0, 0, DateTimeKind.Utc);
        await auto.ProcessItemAsync(item, now, CancellationToken.None);

        Assert.Equal(QueueItemStatus.AppReady, (await _repo.GetAllAsync()).Single().Status);
        Assert.Equal(0, Interlocked.CompareExchange(ref submitCalls, 0, 0));

        Assert.Null(await auto.DrainDueAsync(now, CancellationToken.None));
        Assert.Equal(0, Interlocked.CompareExchange(ref submitCalls, 0, 0));
        Assert.Equal(QueueItemStatus.AppReady, (await _repo.GetAllAsync()).Single().Status);

        auto.ManualBrowserFlowActive = false;
        var drained = await auto.DrainDueAsync(now, CancellationToken.None);
        Assert.NotNull(drained);
        var row = (await _repo.GetAllAsync()).Single();
        Assert.Equal(QueueItemStatus.Submitted, row.Status);
        Assert.Equal(1, Interlocked.CompareExchange(ref submitCalls, 0, 0));
        Assert.NotNull(row.UpdatedAt); // queue transitions stamp their truth
    }

    [Fact]
    public async Task NightOwl_TrustedHostWithoutFullAutoEnabled_DoesNotDrain()    {
        // The master switch is off: a parked row must never be released, hour
        // or no hour — the drain respects the same policy the gate does.
        var settings = new EnvoySettings
        {
            FullAutoEnabled = false,
            FullAutoTrustedHosts = { "boards.greenhouse.io" }
        };
        var gate = new QueueFullAutoGate(
            new ApplicationLogRepository(_factory, Mock.Of<ILogger<ApplicationLogRepository>>()), () => settings);
        var submitter = new StubSubmitter((_, log) =>
        {
            log.VerifiedSubmission = true;
            return (log, null, false);
        });
        var auto = new ApplicationQueueService(_repo, _preparer, Mock.Of<ILogger<ApplicationQueueService>>(),
            () => settings, submitter, gate);

        _preparer.Handler = _ => Task.FromResult(Guid.NewGuid());
        await auto.EnqueueAsync(Item("k-off", "https://boards.greenhouse.io/acme/jobs/902", "Acme"), autoStart: false);
        var item = (await _repo.GetAllAsync()).Single();
        await auto.ProcessItemAsync(item, DateTime.UtcNow, CancellationToken.None);

        Assert.Null(await auto.DrainDueAsync(new DateTime(2026, 11, 5, 23, 0, 0, DateTimeKind.Utc), CancellationToken.None));
        Assert.Equal(QueueItemStatus.AppReady, (await _repo.GetAllAsync()).Single().Status);
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
