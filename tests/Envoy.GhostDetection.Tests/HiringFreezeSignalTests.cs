using Envoy.Core.Services;
using Envoy.GhostDetection.Models;
using Envoy.GhostDetection.Services;
using Envoy.GhostDetection.Signals;
using System.Text.Json;
using Xunit;

namespace Envoy.GhostDetection.Tests;

public class HiringFreezeSignalTests
{
    private static readonly HiringFreezeSignal Signal = new();

    // Dates are anchored to the test dataset's latest Acme notice (2026-10-02)
    // so the 90-day window arithmetic is deterministic. A posting must come
    // AFTER a notice for the correlation to be meaningful.
    private static DateTime PostDate(int daysAfterNotice) =>
        new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc).AddDays(daysAfterNotice);

    private static readonly List<WarnNotice> TestNotices = new()
    {
        new() { Employer = "Acme Widgets Inc.", State = "WA", Location = "Sumner", NoticeDate = new DateTime(2026, 10, 2), AffectedCount = 95 },
        new() { Employer = "Acme Widgets Inc.", State = "WA", Location = "Moses Lake", NoticeDate = new DateTime(2026, 9, 18), AffectedCount = 439 },
        new() { Employer = "Pied Piper LLC", State = "CA", Location = "Menlo Park", NoticeDate = new DateTime(2026, 8, 20), AffectedCount = 120 },
        new() { Employer = "Stale Corp", State = "TX", Location = "Dallas", NoticeDate = new DateTime(2026, 6, 1), AffectedCount = 500 },
        new() { Employer = "Tiny Shoe Company", State = "OR", Location = "Portland", NoticeDate = new DateTime(2026, 9, 25), AffectedCount = 9 },
        new() { Employer = "Grrêts Çörp", State = "WA", Location = "Redmond", NoticeDate = new DateTime(2026, 10, 15), AffectedCount = 0 }
    };

    private static JobPosting Posting(
        string company,
        DateTime postedAt,
        string? location = "Sumner, WA",
        JobSource source = JobSource.Greenhouse)
    {
        var posting = new JobPosting
        {
            CompanyName = company,
            JobTitle = "Warehouse Associate",
            Location = location ?? string.Empty,
            DescriptionText = "Move boxes and keep the floor safe.",
            PostedAtUtc = postedAt,
            Source = source
        };
        // Tests hand the signal its dataset inline (like dupcheck.corpus) so
        // nothing depends on the bundled seed's exact contents or freshness.
        posting.Extra["warn.dataset"] = JsonSerializer.Serialize(TestNotices);
        return posting;
    }

    // ── Fires ──────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_RecentNoticeSameState_FiresProbabilistic()
    {
        var result = await Signal.EvaluateAsync(Posting("Acme Widgets Inc.", PostDate(10)));

        Assert.NotNull(result);
        Assert.Equal("Hiring Freeze", result.SignalName);
        Assert.Equal(SignalTier.Probabilistic, result.Tier);
        // Low-to-moderate: 0.45 fresh start, small single-site bump, capped.
        Assert.True(result.Score >= 0.40 && result.Score <= 0.60,
            $"Expected Score in [0.40, 0.60], got {result.Score}");
        Assert.True(result.Confidence >= 0.55 && result.Confidence <= 0.80,
            $"Expected Confidence in [0.55, 0.80], got {result.Confidence}");
        Assert.NotEmpty(result.Evidence);
        Assert.Contains(result.Evidence, line => line.Contains("filed a WARN notice for 95 workers"));
        Assert.Contains(result.Evidence, line => line.Contains("does not mean this specific role was cut"));
    }

    [Fact]
    public async Task EvaluateAsync_LegalSuffix_VariantsStillMatch()
    {
        // "Acme Widgets, Incorporated" normalizes to the same employer as
        // "Acme Widgets Inc." in the notice.
        var result = await Signal.EvaluateAsync(Posting("Acme Widgets, Incorporated", PostDate(5)));
        Assert.NotNull(result);
    }

    [Fact]
    public async Task EvaluateAsync_MultipleNotices_RaiseConfidence()
    {
        // Acme has two notices inside the window; Pied Piper has one. Same
        // posting age and both inside their employer's state.
        var two = await Signal.EvaluateAsync(Posting("Acme Widgets Inc.", PostDate(25), "Sumner, WA"));
        var one = await Signal.EvaluateAsync(Posting("Pied Piper LLC", PostDate(25), "Menlo Park, CA"));

        Assert.NotNull(two);
        Assert.NotNull(one);
        Assert.True(two.Confidence > one.Confidence,
            $"Two notices should raise confidence: {two.Confidence} vs {one.Confidence}");
    }

    // ── Does not fire: stale, weak, mismatched ─────────────────────────

    [Fact]
    public async Task EvaluateAsync_StaleNotice_ReturnsNull()
    {
        // Stale Corp's notice is > 90 days before the posting date.
        var result = await Signal.EvaluateAsync(Posting("Stale Corp", PostDate(0), "Dallas, TX"));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_NoticeOlderThanWindow_ReturnsNull()
    {
        var result = await Signal.EvaluateAsync(Posting("Tiny Shoe Company", PostDate(100), "Portland, OR"));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_WeakNameMatch_ReturnsNull()
    {
        // "Widgets Co" shares a word with "Acme Widgets Inc." but similarity is low.
        var result = await Signal.EvaluateAsync(Posting("Widgets Co", PostDate(5)));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_DifferentStateLocation_ReturnsNull()
    {
        // Pied Piper's notice is in CA; the posting clearly sits in WA.
        var result = await Signal.EvaluateAsync(Posting("Pied Piper LLC", PostDate(5), "Seattle, WA"));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_DifferentStateFullName_ReturnsNull()
    {
        // "Tacoma, Washington" — a state clearly different from the CA notice.
        var result = await Signal.EvaluateAsync(Posting("Pied Piper LLC", PostDate(5), "Tacoma, Washington"));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_UnknownAmbiguousLocation_StillFiresWithCaveat()
    {
        var result = await Signal.EvaluateAsync(Posting("Acme Widgets Inc.", PostDate(10), location: "Remote"));

        Assert.NotNull(result);
        // Score is edge-case fine here; evidence must carry the caution line.
        Assert.Contains(result.Evidence, line => line.Contains("could not be compared with the state"));
    }

    // ── Missing data → null, never throws ──────────────────────────────

    [Fact]
    public async Task EvaluateAsync_MissingCompanyName_ReturnsNull()
    {
        var result = await Signal.EvaluateAsync(Posting("", PostDate(5)));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_UnicodeEmployerName_DoesNotThrow()
    {
        var result = await Signal.EvaluateAsync(Posting("Grrêts Çörp", PostDate(20), "Redmond, WA"));
        // AffectedCount 0 and a diacritic name both pass the try/catch cleanly.
        Assert.NotNull(result);
        Assert.Equal(SignalTier.Probabilistic, result!.Tier);
    }

    [Fact]
    public async Task EvaluateAsync_EmptyDataset_ReturnsNull()
    {
        try
        {
            WarnDataset.LoadOverride = () => new List<WarnNotice>();
            var posting = new JobPosting
            {
                CompanyName = "Acme Widgets Inc.",
                Location = "Sumner, WA",
                PostedAtUtc = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc)
            };
            Assert.Null(await Signal.EvaluateAsync(posting));
        }
        finally
        {
            WarnDataset.LoadOverride = null;
        }
    }

    [Fact]
    public async Task EvaluateAsync_LoaderThrows_ReturnsNull()
    {
        // The signal's outer catch keeps a broken loader from ever throwing.
        try
        {
            WarnDataset.LoadOverride = () => throw new IOException("disk exploded");
            var posting = new JobPosting
            {
                CompanyName = "Acme Widgets Inc.",
                Location = "Sumner, WA",
                PostedAtUtc = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc)
            };
            Assert.Null(await Signal.EvaluateAsync(posting));
        }
        finally
        {
            WarnDataset.LoadOverride = null;
        }
    }

    // ── Fixtures ───────────────────────────────────────────────────────

    [Fact]
    public void FixtureWarnDataset_DeserializesWithLabeledValues()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(HiringFreezeSignalTests).Assembly.Location)!;
        var fixturePath = Path.Combine(assemblyDir, "..", "..", "..", "fixtures", "warn-notices-test.json");
        var notices = JsonSerializer.Deserialize<List<WarnNotice>>(File.ReadAllText(fixturePath));

        Assert.NotNull(notices);
        Assert.Equal(6, notices.Count);
        var acme = notices.First(n => n.Employer.StartsWith("Acme"));
        Assert.Equal(new DateTime(2026, 10, 2), acme.NoticeDate);
        Assert.Equal("WA", acme.State);
        Assert.Equal(95, acme.AffectedCount);
    }

    [Fact]
    public async Task EvaluateAsync_InlineDatasetViaExtra_Fires()
    {
        // DuplicateJdSignal-style: a caller may hand the dataset inline so a
        // posting skips the shared cache (used by tests and batch scoring).
        var posting = new JobPosting
        {
            CompanyName = "Acme Widgets Inc.",
            Location = "Sumner, WA",
            PostedAtUtc = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc)
        };
        posting.Extra["warn.dataset"] = JsonSerializer.Serialize(new List<WarnNotice>
        {
            new() { Employer = "Acme Widgets Inc.", State = "WA", Location = "Sumner", NoticeDate = new DateTime(2026, 9, 10), AffectedCount = 95 }
        });

        var result = await Signal.EvaluateAsync(posting);
        Assert.NotNull(result);
        Assert.Equal(SignalTier.Probabilistic, result.Tier);
    }

    // ── Bundled seed sanity ────────────────────────────────────────────

    [Fact]
    public void BundledSeed_LoadsRealNotices()
    {
        // The bundled seed ships next to the app (transitively copied into the
        // test output too). Guard against a broken or empty seed slipping in.
        // Read the file directly rather than going through the shared
        // in-memory cache: parallel suites legitimately reset or replace the
        // cached dataset (the refresher suite does both), and this check is
        // about the seed file on disk, not the runtime cache.
        var seedPath = WarnDataset.BundledPath;
        Assert.True(File.Exists(seedPath), $"bundled seed missing at {seedPath}");

        var json = File.ReadAllText(seedPath);
        var stripped = string.Join('\n', json.Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
        var notices = JsonSerializer.Deserialize<List<WarnNotice>>(stripped);

        Assert.NotNull(notices);
        Assert.True(notices!.Count >= 100, $"Expected the real bundled seed, got {notices.Count} notices");
        Assert.All(notices, n =>
        {
            Assert.False(string.IsNullOrWhiteSpace(n.Employer));
            Assert.False(string.IsNullOrWhiteSpace(n.State));
            Assert.True(n.NoticeDate >= new DateTime(2020, 1, 1));
        });
    }
}
