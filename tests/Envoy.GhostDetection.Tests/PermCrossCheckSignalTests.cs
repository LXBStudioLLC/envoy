using Envoy.GhostDetection.Models;
using Envoy.GhostDetection.Services;
using Envoy.GhostDetection.Signals;
using System.Text.Json;
using Xunit;

namespace Envoy.GhostDetection.Tests;

public class PermCrossCheckSignalTests
{
    private static readonly PermCrossCheckSignal Signal = new();

    // "Now" for the 12-month window arithmetic inside the signal is UtcNow,
    // so every fixture filing here is dated well inside 12 months of 2026-11-05.
    private static readonly List<PermFiling> TestFilings = new()
    {
        new() { Employer = "Narnia Interactive Inc.", JobTitle = "Senior Software Engineer", State = "CA", SocCode = "15-1252.00", ReceivedDate = new DateTime(2026, 5, 14) },
        new() { Employer = "Narnia Interactive Inc.", JobTitle = "Software Engineer", State = "CA", SocCode = "15-1252.00", ReceivedDate = new DateTime(2026, 3, 2) },
        new() { Employer = "Bottleneck Analytics LLC", JobTitle = "Data Scientist", State = "NY", SocCode = "15-2051.00", ReceivedDate = new DateTime(2026, 1, 20) },
        new() { Employer = "Old Harbor Press Corp", JobTitle = "Content Strategist", State = "TX", SocCode = "27-3031.00", ReceivedDate = new DateTime(2026, 2, 11) },
        new() { Employer = "Lakeshore Systems, Ltd", JobTitle = "Marketing Coordinator", State = "WI", SocCode = "11-2021.00", ReceivedDate = new DateTime(2024, 2, 1) }
    };

    private static JobPosting Posting(string? company, string title, string? location = "Oakland, CA")
    {
        var posting = new JobPosting
        {
            CompanyName = company ?? string.Empty,
            JobTitle = title,
            Location = location ?? string.Empty,
            DescriptionText = "Design and ship features with a small team."
        };
        posting.Extra["perm.dataset"] = JsonSerializer.Serialize(TestFilings);
        return posting;
    }

    // ── Fires ──────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_RecentSimilarFiling_FiresProbabilistic()
    {
        var result = await Signal.EvaluateAsync(Posting("Narnia Interactive, Inc.", "Senior Software Engineer"));

        Assert.NotNull(result);
        Assert.Equal("PERM Filing Cross-Check", result.SignalName);
        Assert.Equal(SignalTier.Probabilistic, result.Tier);
        Assert.True(result.Score <= 0.50, $"Expected a low score, got {result.Score}");
        Assert.True(result.Confidence >= 0.50 && result.Confidence <= 0.70,
            $"Expected moderate confidence, got {result.Confidence}");
        Assert.Contains(result.Evidence, line =>
            line.Contains("filed a PERM labor certification for a similar role") &&
            line.Contains("Senior Software Engineer") &&
            line.Contains("CA"));
        // Correlation framing and no verdict line, always.
        Assert.Contains(result.Evidence, line => line.Contains("correlation, not proof"));
    }

    [Fact]
    public async Task EvaluateAsync_TokenOverlapTitle_Matches()
    {
        // Posting phrase differs lexically but shares its tokens with the filing.
        var result = await Signal.EvaluateAsync(Posting("Narnia Interactive, Inc.", "Software Engineer, Senior"));
        Assert.NotNull(result);
    }

    [Fact]
    public async Task EvaluateAsync_MultipleFilings_MentionCount()
    {
        var result = await Signal.EvaluateAsync(Posting("Narnia Interactive, Inc.", "Senior Software Engineer"));
        Assert.NotNull(result);
        // Two distinct Narnia filings (Senior SWE + SWE) both match this title.
        Assert.Contains(result.Evidence, line => line.Contains("2 recent PERM filings"));
    }

    // ── Does not fire ──────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_EmployerOnlyNoTitleMatch_ReturnsNull()
    {
        // A bare employer-name match must not fire on its own: large employers
        // file many PERMs for unrelated roles.
        var result = await Signal.EvaluateAsync(Posting("Narnia Interactive, Inc.", "Head of Recruiting"));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_WeakEmployerMatch_ReturnsNull()
    {
        // "Narnia Games" is similar-ish to "Narnia Interactive Inc." but below
        // the strong-match bar.
        var result = await Signal.EvaluateAsync(Posting("Narnia Games", "Senior Software Engineer"));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_DifferentState_ReturnsNull()
    {
        var result = await Signal.EvaluateAsync(Posting("Narnia Interactive, Inc.", "Senior Software Engineer", "New York, NY"));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_DifferentStateFullName_ReturnsNull()
    {
        var result = await Signal.EvaluateAsync(Posting("Narnia Interactive, Inc.", "Senior Software Engineer", "Miami, Florida"));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_StaleFiling_Ignored()
    {
        // 2024 filing — far outside the 12-month window.
        var result = await Signal.EvaluateAsync(Posting("Lakeshore Systems, Ltd", "Marketing Coordinator", "Milwaukee, WI"));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_MissingCompany_ReturnsNull()
    {
        var result = await Signal.EvaluateAsync(Posting("", "Senior Software Engineer"));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_MissingTitle_ReturnsNull()
    {
        var result = await Signal.EvaluateAsync(Posting("Narnia Interactive, Inc.", ""));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_AmbiguousLocation_DoesNotEnforceState()
    {
        // "110 Fifth Ave" — no state signal, so the state check can't be
        // enforced; the employer+title match stands on its own.
        var result = await Signal.EvaluateAsync(Posting("Bottleneck Analytics, LLC", "Data Scientist", "110 Fifth Ave"));
        Assert.NotNull(result);
    }

    // ── Missing data → null, never throws ──────────────────────────────

    [Fact]
    public async Task EvaluateAsync_EmptyDataset_ReturnsNull()
    {
        try
        {
            PermIndex.LoadOverride = () => new List<PermFiling>();
            var posting = new JobPosting { CompanyName = "Narnia Interactive, Inc.", JobTitle = "Senior Software Engineer", Location = "Oakland, CA" };
            Assert.Null(await Signal.EvaluateAsync(posting));
        }
        finally
        {
            PermIndex.LoadOverride = null;
        }
    }

    [Fact]
    public async Task EvaluateAsync_LoaderThrows_ReturnsNull()
    {
        try
        {
            PermIndex.LoadOverride = () => throw new IOException("disk exploded");
            var posting = new JobPosting { CompanyName = "Narnia Interactive, Inc.", JobTitle = "Senior Software Engineer", Location = "Oakland, CA" };
            Assert.Null(await Signal.EvaluateAsync(posting));
        }
        finally
        {
            PermIndex.LoadOverride = null;
        }
    }

    // ── Fixtures ───────────────────────────────────────────────────────

    [Fact]
    public void FixturePermDataset_ParsesLabeledFilings()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(PermCrossCheckSignalTests).Assembly.Location)!;
        var fixturePath = Path.Combine(assemblyDir, "..", "..", "..", "fixtures", "perm-filings-test.json");
        var filings = JsonSerializer.Deserialize<List<PermFiling>>(File.ReadAllText(fixturePath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(filings);
        Assert.Equal(5, filings.Count);
        var narnia = filings.First(f => f.Employer.StartsWith("Narnia"));
        Assert.Equal(new DateTime(2026, 5, 14), narnia.ReceivedDate);
        Assert.Equal("CA", narnia.State);
        Assert.Equal("15-1252.00", narnia.SocCode);
    }

    [Fact]
    public void BundledSeed_LoadsFilingsFromRealIndex()
    {
        // The bundled perm-index.json ships transitively into the test output;
        // a broken, empty, or unparseable seed must fail here loudly. Read the
        // file directly rather than through the shared runtime cache: the
        // builder suites legitimately replace or reset that cache, and this
        // check is about the seed file on disk.
        var seedPath = PermIndex.BundledPath;
        Assert.True(File.Exists(seedPath), $"bundled seed missing at {seedPath}");

        var envelope = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(seedPath));
        var rows = envelope.GetProperty("Rows").EnumerateArray().ToList();
        Assert.True(rows.Count >= 1000, $"Expected the real bundled index (>=1000 filings), got {rows.Count}");

        foreach (var row in rows)
        {
            Assert.False(string.IsNullOrWhiteSpace(row[0].GetString()));
            Assert.False(string.IsNullOrWhiteSpace(row[1].GetString()));
            var state = row[2].GetString() ?? string.Empty;
            Assert.True(state.Length == 0 || state.Length == 2, $"bad state {state}");
            Assert.True(DateTime.TryParse(row[4].GetString(), out var received) &&
                        received >= new DateTime(2025, 11, 1), $"bad date {row[4].GetString()}");
        }
    }
}
