using Envoy.GhostDetection.Models;
using Envoy.GhostDetection.Signals;
using System.Text.Json;
using Xunit;

namespace Envoy.GhostDetection.Tests;

public class RepostFrequencySignalTests
{
    private static readonly RepostFrequencySignal Signal = new();

    private static JobPosting BuildPosting(
        string description,
        DateTime? postedAt = null,
        DateTime? lastUpdated = null,
        string? historyJson = null)
    {
        var extra = new Dictionary<string, string>();
        if (historyJson != null)
            extra["repost.history"] = historyJson;

        return new JobPosting
        {
            CompanyName = "Acme Corp",
            JobTitle = "Software Engineer",
            DescriptionText = description,
            PostedAtUtc = postedAt,
            LastUpdatedUtc = lastUpdated,
            Extra = extra
        };
    }

    // A long description (>= 200 chars) for PATH A eligibility.
    private static readonly string LongDesc =
        "We are seeking a passionate software engineer to join our growing team. " +
        "You will design, develop, and maintain high-quality software applications. " +
        "The ideal candidate has strong problem-solving skills and experience with modern frameworks. " +
        "Responsibilities include writing clean code, performing code reviews, and collaborating with cross-functional teams. " +
        "Requirements: 3+ years of experience, proficiency in C# and .NET, familiarity with cloud platforms. " +
        "Benefits include competitive salary, health insurance, and flexible working hours. " +
        "We value diversity and inclusion and are an equal opportunity employer. " +
        "Apply today to be part of our innovative journey.";

    // A different description, same length, same vocabulary domain.
    private static readonly string DifferentDesc =
        "Our engineering organization is hiring talented developers. " +
        "You will build scalable systems and contribute to open-source tools. " +
        "The right person has a background in distributed systems and enjoys mentoring. " +
        "Day-to-day work involves architecture decisions, pair programming, and release automation. " +
        "We look for 5+ years of hands-on coding, deep knowledge of Python or Go, and comfort with Kubernetes. " +
        "Perks include unlimited PTO, remote-first culture, and equity participation. " +
        "We believe in sustainable pace and psychological safety for every team member. " +
        "Send us your portfolio and let us build something meaningful together.";

    // A partially different description (will be ~70-80% similar).
    private static readonly string PartiallyDifferentDesc =
        "We are seeking a passionate software engineer to join our growing team. " +
        "You will design, develop, and maintain high-quality software applications. " +
        "The ideal candidate has strong problem-solving skills and experience with modern frameworks. " +
        "Responsibilities include writing clean code, performing code reviews, and collaborating with cross-functional teams. " +
        "Requirements: 3+ years of experience, proficiency in C# and .NET, familiarity with cloud platforms. " +
        "Benefits include competitive salary, health insurance, and flexible working hours. " +
        "We are a fast-paced startup looking for self-starters who thrive under pressure. " +
        "Submit your resume and a cover letter explaining why you are the perfect fit.";

    private static string BuildHistoryJson(params (DateTime SeenAtUtc, string Description)[] entries)
    {
        var list = entries.Select(e => new { seenAtUtc = e.SeenAtUtc, description = e.Description }).ToList();
        return JsonSerializer.Serialize(list);
    }

    // 1. No timestamps, no history → null.
    [Fact]
    public async Task EvaluateAsync_NoTimestampsNoHistory_ReturnsNull()
    {
        var posting = BuildPosting(LongDesc);
        var result = await Signal.EvaluateAsync(posting);
        Assert.Null(result);
    }

    // 2. PostedAtUtc == LastUpdatedUtc, no history → null.
    [Fact]
    public async Task EvaluateAsync_EqualTimestampsNoHistory_ReturnsNull()
    {
        var now = DateTime.UtcNow;
        var posting = BuildPosting(LongDesc, postedAt: now, lastUpdated: now);
        var result = await Signal.EvaluateAsync(posting);
        Assert.Null(result);
    }

    // 3. Gap 20 days, no history → null.
    [Fact]
    public async Task EvaluateAsync_ShortGapNoHistory_ReturnsNull()
    {
        var posted = DateTime.UtcNow.AddDays(-20);
        var updated = DateTime.UtcNow;
        var posting = BuildPosting(LongDesc, postedAt: posted, lastUpdated: updated);
        var result = await Signal.EvaluateAsync(posting);
        Assert.Null(result);
    }

    // 4. Gap 60 days, no history → fires: Score 0.35, Confidence 0.40, Tier Weak.
    [Fact]
    public async Task EvaluateAsync_LongGapNoHistory_FiresPathB()
    {
        var posted = DateTime.UtcNow.AddDays(-60);
        var updated = DateTime.UtcNow;
        var posting = BuildPosting(LongDesc, postedAt: posted, lastUpdated: updated);

        var result = await Signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.Equal("Repost Frequency", result.SignalName);
        Assert.Equal(0.35, result.Score);
        Assert.Equal(0.40, result.Confidence);
        Assert.Equal(SignalTier.Weak, result.Tier);
        Assert.Contains("60 days", result.Evidence[0]);
    }

    // 5. Malformed repost.history JSON → null (no throw).
    [Fact]
    public async Task EvaluateAsync_MalformedHistoryJson_ReturnsNull()
    {
        var posting = BuildPosting(LongDesc, historyJson: "not-valid-json");
        var result = await Signal.EvaluateAsync(posting);
        Assert.Null(result);
    }

    // 6. Three identical snapshots (2 unchanged bumps) → fires: Score 0.45, Confidence 0.60.
    [Fact]
    public async Task EvaluateAsync_ThreeIdenticalSnapshots_FiresPathA()
    {
        var t1 = DateTime.UtcNow.AddDays(-40);
        var t2 = DateTime.UtcNow.AddDays(-20);
        var history = BuildHistoryJson(
            (t1, LongDesc),
            (t2, LongDesc));
        var posting = BuildPosting(LongDesc, lastUpdated: DateTime.UtcNow, historyJson: history);

        var result = await Signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.Equal(0.45, result.Score);
        Assert.Equal(0.60, result.Confidence);
        Assert.Equal(SignalTier.Weak, result.Tier);
    }

    // 7. Five identical snapshots within 14 days → Score 0.75 (0.65 + 0.10 cap respected),
    //    Confidence 0.70, evidence has the rapid-cycling sentence.
    [Fact]
    public async Task EvaluateAsync_FiveIdenticalSnapshotsWithin14Days_FiresWithRapidCycle()
    {
        var baseTime = DateTime.UtcNow.AddDays(-14);
        var history = BuildHistoryJson(
            (baseTime, LongDesc),
            (baseTime.AddDays(2), LongDesc),
            (baseTime.AddDays(4), LongDesc),
            (baseTime.AddDays(6), LongDesc));
        var posting = BuildPosting(LongDesc, lastUpdated: DateTime.UtcNow, historyJson: history);

        var result = await Signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.Equal(0.75, result.Score);
        Assert.Equal(0.70, result.Confidence);
        Assert.Equal(SignalTier.Weak, result.Tier);
        Assert.Contains("rapid", result.Evidence[1].ToLowerInvariant());
    }

    // 8. Snapshots where each successive pair materially differs (similarity < 0.95) → null.
    [Fact]
    public async Task EvaluateAsync_AllSnapshotsChanged_ReturnsNull()
    {
        var t1 = DateTime.UtcNow.AddDays(-14);
        var t2 = DateTime.UtcNow.AddDays(-7);
        var history = BuildHistoryJson(
            (t1, LongDesc),
            (t2, DifferentDesc));
        var posting = BuildPosting(DifferentDesc, lastUpdated: DateTime.UtcNow, historyJson: history);

        var result = await Signal.EvaluateAsync(posting);

        Assert.Null(result);
    }

    // 9. Mixed: one real edit followed by two unchanged bumps → fires.
    [Fact]
    public async Task EvaluateAsync_MixedEditThenUnchanged_Fires()
    {
        var t1 = DateTime.UtcNow.AddDays(-60);
        var t2 = DateTime.UtcNow.AddDays(-40);
        var t3 = DateTime.UtcNow.AddDays(-20);
        var history = BuildHistoryJson(
            (t1, LongDesc),
            (t2, PartiallyDifferentDesc),
            (t3, PartiallyDifferentDesc));
        var posting = BuildPosting(PartiallyDifferentDesc, lastUpdated: DateTime.UtcNow, historyJson: history);

        var result = await Signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.Equal(SignalTier.Weak, result.Tier);
        Assert.Equal(0.45, result.Score);
        Assert.Equal(0.60, result.Confidence);
    }

    // 10. Snapshot below 200-char guard is skipped, not crashed on.
    [Fact]
    public async Task EvaluateAsync_BelowSizeGuardSnapshot_SkippedQuietly()
    {
        var t1 = DateTime.UtcNow.AddDays(-14);
        var t2 = DateTime.UtcNow.AddDays(-7);
        var history = BuildHistoryJson(
            (t1, LongDesc),
            (t2, "Short description."),
            (DateTime.UtcNow.AddDays(-3), LongDesc));
        var posting = BuildPosting(LongDesc, lastUpdated: DateTime.UtcNow, historyJson: history);

        var result = await Signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.Equal(SignalTier.Weak, result.Tier);
    }

    // 11. Unsorted history input is sorted by seenAtUtc before pairing.
    // Falsifiable: scrambled order would yield 0 bumps (null) if not sorted.
    [Fact]
    public async Task EvaluateAsync_UnsortedHistory_SortedBeforePairing()
    {
        var t1 = DateTime.UtcNow.AddDays(-60);
        var t2 = DateTime.UtcNow.AddDays(-40);
        var t3 = DateTime.UtcNow.AddDays(-20);
        var history = BuildHistoryJson(
            (t2, LongDesc),       // scrambled order
            (t3, DifferentDesc),
            (t1, LongDesc));
        var posting = BuildPosting(DifferentDesc, lastUpdated: DateTime.UtcNow, historyJson: history);

        var result = await Signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.Equal(0.45, result.Score);
        Assert.Equal(0.60, result.Confidence);
    }

    // 12. History present AND a 60-day timestamp gap → PATH A result only (history supersedes).
    [Fact]
    public async Task EvaluateAsync_HistoryPresentWithLongGap_UsesPathA()
    {
        var posted = DateTime.UtcNow.AddDays(-60);
        var updated = DateTime.UtcNow;
        var history = BuildHistoryJson(
            (posted, LongDesc),
            (posted.AddDays(30), LongDesc));
        var posting = BuildPosting(LongDesc, postedAt: posted, lastUpdated: updated, historyJson: history);

        var result = await Signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.Equal(0.45, result.Score);
        Assert.Equal(0.60, result.Confidence);
        Assert.NotEqual(0.35, result.Score);
    }

    // 13. Current posting description below 200 chars → PATH A returns null.
    [Fact]
    public async Task EvaluateAsync_CurrentDescriptionTooShort_ReturnsNull()
    {
        var history = BuildHistoryJson(
            (DateTime.UtcNow.AddDays(-7), LongDesc),
            (DateTime.UtcNow.AddDays(-3), LongDesc));
        var posting = BuildPosting("Short description.", lastUpdated: DateTime.UtcNow, historyJson: history);

        var result = await Signal.EvaluateAsync(posting);

        Assert.Null(result);
    }

    // 14. Only one usable snapshot (< 2) → PATH A returns null, PATH B may catch.
    [Fact]
    public async Task EvaluateAsync_OneUsableSnapshot_PathAFailsPathBCatches()
    {
        var posted = DateTime.UtcNow.AddDays(-60);
        var updated = DateTime.UtcNow;
        var history = BuildHistoryJson(
            (posted, LongDesc));
        var posting = BuildPosting(LongDesc, postedAt: posted, lastUpdated: updated, historyJson: history);

        var result = await Signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.Equal(0.35, result.Score);
        Assert.Equal(0.40, result.Confidence);
    }

    // 15. Two identical snapshots but span > 30 days → no rapid-cycle bonus.
    [Fact]
    public async Task EvaluateAsync_TwoBumpsOver40Days_NoRapidCycleBonus()
    {
        var baseTime = DateTime.UtcNow.AddDays(-40);
        var history = BuildHistoryJson(
            (baseTime, LongDesc),
            (baseTime.AddDays(20), LongDesc));
        var posting = BuildPosting(LongDesc, lastUpdated: DateTime.UtcNow, historyJson: history);

        var result = await Signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.Equal(0.45, result.Score);
        Assert.Equal(0.60, result.Confidence);
        Assert.Single(result.Evidence);
    }

    // 16. Load fixture posting-repost-bumped.json and assert PATH A fires.
    [Fact]
    public async Task EvaluateAsync_Fixture_RepostBumped_Fires()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(RepostFrequencySignalTests).Assembly.Location)!;
        var fixturePath = Path.Combine(assemblyDir, "..", "..", "..", "fixtures", "posting-repost-bumped.json");
        var raw = File.ReadAllText(fixturePath);
        var stripped = string.Join("\n", raw.Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
        using var doc = JsonDocument.Parse(stripped);
        var root = doc.RootElement;

        var descriptionText = root.GetProperty("DescriptionText").GetString();
        Assert.NotNull(descriptionText);

        var lastUpdated = root.GetProperty("LastUpdatedUtc").GetDateTime();

        var extra = new Dictionary<string, string>();
        if (root.TryGetProperty("Extra", out var extraElement))
        {
            foreach (var prop in extraElement.EnumerateObject())
            {
                extra[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }
        }

        var fixture = new JobPosting
        {
            CompanyName = root.GetProperty("CompanyName").GetString() ?? "",
            JobTitle = root.GetProperty("JobTitle").GetString() ?? "",
            DescriptionText = descriptionText,
            LastUpdatedUtc = lastUpdated,
            Extra = extra
        };

        var result = await Signal.EvaluateAsync(fixture);

        Assert.NotNull(result);
        Assert.Equal(0.65, result.Score);
        Assert.Equal(0.70, result.Confidence);
        Assert.Equal(SignalTier.Weak, result.Tier);
        Assert.Equal(2, result.Evidence.Length);
        Assert.Contains("rapid", result.Evidence[1].ToLowerInvariant());
    }
}
