using Envoy.GhostDetection.Models;
using Envoy.GhostDetection.Signals;
using System.Text.Json;
using Xunit;

namespace Envoy.GhostDetection.Tests;

public class PostingAgeSignalTests
{
    private static readonly PostingAgeSignal Signal = new();

    [Fact]
    public async Task EvaluateAsync_StaleJuniorFixture_FiresProbabilistic()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(PostingAgeSignalTests).Assembly.Location)!;
        var fixturePath = Path.Combine(assemblyDir, "..", "..", "..", "fixtures", "posting-age-stale-junior.json");
        var raw = File.ReadAllText(fixturePath);
        var stripped = string.Join("\n", raw.Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
        var fixture = JsonSerializer.Deserialize<JobPosting>(stripped)!;

        var result = await Signal.EvaluateAsync(fixture);

        Assert.NotNull(result);
        Assert.Equal(SignalTier.Probabilistic, result.Tier);
        Assert.True(result.Score > 0.5, $"Expected stale junior fixture to score > 0.5, got {result.Score}");
    }

    [Fact]
    public async Task EvaluateAsync_FreshPosting_ReturnsNull()
    {
        var posting = new JobPosting
        {
            JobTitle = "Software Engineer",
            PostedAtUtc = DateTime.UtcNow.AddDays(-10)
        };

        var result = await Signal.EvaluateAsync(posting);

        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_MissingDates_ReturnsNull()
    {
        var posting = new JobPosting
        {
            JobTitle = "Software Engineer"
            // PostedAtUtc and LastUpdatedUtc are null
        };

        var result = await Signal.EvaluateAsync(posting);

        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_StaleJuniorPosting_ReturnsElevatedScore()
    {
        // Junior baseline is 30 days; 95 days is >3x baseline
        var posting = new JobPosting
        {
            JobTitle = "Junior Data Analyst",
            PostedAtUtc = DateTime.UtcNow.AddDays(-95)
        };

        var result = await Signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.True(result.Score > 0.5, $"Expected elevated score for stale junior role, got {result.Score}");
        Assert.True(result.Confidence >= 0.6);
        Assert.Contains("95 days ago", result.Evidence[0]);
        Assert.Contains("entry-level / junior", result.Evidence[0]);
    }

    [Fact]
    public async Task EvaluateAsync_StaleSeniorPosting_ReturnsModerateScore()
    {
        // Senior baseline is 90 days; 95 days is only slightly over baseline
        // Should NOT be as alarming as a 95-day junior posting
        var posting = new JobPosting
        {
            JobTitle = "Senior Software Engineer",
            PostedAtUtc = DateTime.UtcNow.AddDays(-95)
        };

        var result = await Signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        // 95 days for a senior role (baseline 90) -> score ~0.31
        Assert.True(result.Score < 0.5, $"Expected moderate score for slightly stale senior role, got {result.Score}");
        Assert.Contains("senior / lead", result.Evidence[0]);
    }

    [Fact]
    public async Task EvaluateAsync_VeryStaleExecutivePosting_ReturnsHighScore()
    {
        // Executive baseline is 120 days; 400 days is >3x
        var posting = new JobPosting
        {
            JobTitle = "VP of Engineering",
            PostedAtUtc = DateTime.UtcNow.AddDays(-400)
        };

        var result = await Signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.True(result.Score > 0.7, $"Expected high score for very stale executive role, got {result.Score}");
        Assert.Contains("400 days ago", result.Evidence[0]);
        Assert.Contains("executive / director", result.Evidence[0]);
    }

    [Fact]
    public async Task EvaluateAsync_FallsBackToLastUpdatedUtc()
    {
        var posting = new JobPosting
        {
            JobTitle = "Entry-Level Marketing Coordinator",
            PostedAtUtc = null,
            LastUpdatedUtc = DateTime.UtcNow.AddDays(-100)
        };

        var result = await Signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.True(result.Score > 0.5);
        Assert.Contains("100 days ago", result.Evidence[0]);
    }

    [Fact]
    public async Task EvaluateAsync_AmbiguousTitleDefaultsToMid()
    {
        // "Designer" has no seniority keyword — defaults to mid (60-day baseline)
        // 130 days is >2x baseline, should return a moderate-to-elevated score
        var posting = new JobPosting
        {
            JobTitle = "Designer",
            PostedAtUtc = DateTime.UtcNow.AddDays(-130)
        };

        var result = await Signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.True(result.Score >= 0.55, $"Expected at least moderate score for ambiguous-title stale role, got {result.Score}");
        Assert.Contains("mid-level", result.Evidence[0]);
    }
}
