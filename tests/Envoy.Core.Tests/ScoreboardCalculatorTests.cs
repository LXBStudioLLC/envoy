using Envoy.Core.Models;
using Envoy.Core.Services;
using Xunit;

namespace Envoy.Core.Tests;

public class ScoreboardCalculatorTests
{
    // Fixed local noon in mid-July: far from midnight and from DST transitions,
    // so local/UTC round trips are stable on any machine.
    private static readonly DateTime NowLocal = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Local);

    private static DateTime UtcDaysAgo(int daysAgo) => NowLocal.AddDays(-daysAgo).ToUniversalTime();

    private static JobEvent Ev(JobEventType type, string? band = null, int daysAgo = 0, double? score = null) => new()
    {
        Type = type,
        RiskBand = band,
        RiskScore = score,
        OccurredAt = UtcDaysAgo(daysAgo),
        Company = "Acme",
        JobTitle = "Engineer",
        JobUrl = "https://boards.greenhouse.io/acme/jobs/123",
        PostingKey = "boards.greenhouse.io/acme/jobs/123",
        Evidence = "Closed on the company ATS"
    };

    private static ScoreboardStats Compute(params JobEvent[] events) =>
        ScoreboardCalculator.Compute(events, Array.Empty<ApplicationLog>(), 45, NowLocal);

    private static ApplicationLog Submit(ApplicationStatus status, ResponseOutcome outcome = ResponseOutcome.None) => new()
    {
        Status = status,
        Outcome = outcome,
        JobUrl = "https://boards.greenhouse.io/acme/jobs/123",
        JobTitle = "Engineer",
        Company = "Acme"
    };

    [Fact]
    public void Dodges_AreExplicitActsOnFlaggedPostingsOnly()
    {
        var stats = Compute(
            Ev(JobEventType.Skipped, "High"),
            Ev(JobEventType.Declined, "Elevated"),
            Ev(JobEventType.Skipped, "Neutral"),   // skip of a clean posting: not a dodge
            Ev(JobEventType.Skipped),              // unscored skip: not a dodge
            Ev(JobEventType.Sighted, "High"),      // flagged but only seen: never a dodge
            Ev(JobEventType.Applied, "High"));     // applied anyway: not a dodge

        Assert.Equal(2, stats.GhostsDodged);
    }

    [Fact]
    public void HoursSaved_IsDodgesTimesMinutes_AndShowsItsInputs()
    {
        var events = new[]
        {
            Ev(JobEventType.Skipped, "High"),
            Ev(JobEventType.Skipped, "High"),
            Ev(JobEventType.Declined, "Elevated")
        };

        var stats = ScoreboardCalculator.Compute(events, Array.Empty<ApplicationLog>(), 40, NowLocal);

        Assert.Equal(2.0, stats.HoursSaved);
        Assert.Equal(40, stats.MinutesPerApplication);
    }

    [Fact]
    public void NonPositiveMinutes_FallBackToDefault()
    {
        var stats = ScoreboardCalculator.Compute(
            new[] { Ev(JobEventType.Skipped, "High") }, Array.Empty<ApplicationLog>(), 0, NowLocal);

        Assert.Equal(45, stats.MinutesPerApplication);
        Assert.Equal(0.8, stats.HoursSaved);
    }

    [Fact]
    public void VillainStats_CountSightingsAndFlaggedSightings()
    {
        var stats = Compute(
            Ev(JobEventType.Sighted, "Neutral"),
            Ev(JobEventType.Sighted, "High"),
            Ev(JobEventType.Sighted, "Elevated"),
            Ev(JobEventType.Viewed, "High"),      // a view is not a sighting
            Ev(JobEventType.Applied));

        Assert.Equal(3, stats.PostingsScreened);
        Assert.Equal(2, stats.GhostsSurfaced);
        Assert.Equal(1, stats.Applications);
    }

    [Fact]
    public void Streak_CountsConsecutiveActiveDays_EndingTodayOrYesterday()
    {
        Assert.Equal(3, Compute(
            Ev(JobEventType.Sighted, daysAgo: 0),
            Ev(JobEventType.Sighted, daysAgo: 0),   // same day counts once
            Ev(JobEventType.Viewed, daysAgo: 1),
            Ev(JobEventType.Applied, daysAgo: 2)).StreakDays);

        // Nothing yet today: the run ending yesterday still stands.
        Assert.Equal(2, Compute(
            Ev(JobEventType.Sighted, daysAgo: 1),
            Ev(JobEventType.Sighted, daysAgo: 2)).StreakDays);

        // A gap before yesterday breaks it.
        Assert.Equal(1, Compute(
            Ev(JobEventType.Sighted, daysAgo: 0),
            Ev(JobEventType.Sighted, daysAgo: 2)).StreakDays);

        // Last activity two days ago: no live streak.
        Assert.Equal(0, Compute(Ev(JobEventType.Sighted, daysAgo: 2)).StreakDays);

        Assert.Equal(0, Compute().StreakDays);
    }

    [Fact]
    public void ResponseRate_HiddenUntilFiveCompletedSubmits()
    {
        var submits = new[]
        {
            Submit(ApplicationStatus.Completed, ResponseOutcome.Interview),
            Submit(ApplicationStatus.Completed),
            Submit(ApplicationStatus.Completed),
            Submit(ApplicationStatus.Completed),
            Submit(ApplicationStatus.Failed, ResponseOutcome.Replied)   // not completed, ignored entirely
        };

        var stats = ScoreboardCalculator.Compute(Array.Empty<JobEvent>(), submits, 45, NowLocal);

        Assert.Equal(4, stats.SubmitsCompleted);
        Assert.Equal(1, stats.Responses);
        Assert.Null(stats.ResponseRatePercent);
    }

    [Fact]
    public void ResponseRate_CountsAnyAnswerIncludingRejection()
    {
        var submits = new[]
        {
            Submit(ApplicationStatus.Completed, ResponseOutcome.Replied),
            Submit(ApplicationStatus.Completed, ResponseOutcome.Interview),
            Submit(ApplicationStatus.Completed, ResponseOutcome.Offer),
            Submit(ApplicationStatus.Completed, ResponseOutcome.Rejected),
            Submit(ApplicationStatus.Completed),
            Submit(ApplicationStatus.Completed)
        };

        var stats = ScoreboardCalculator.Compute(Array.Empty<JobEvent>(), submits, 45, NowLocal);

        Assert.Equal(6, stats.SubmitsCompleted);
        Assert.Equal(4, stats.Responses);
        Assert.Equal(67, stats.ResponseRatePercent);
    }

    [Fact]
    public void Receipts_AreFlaggedDodgesNewestFirst_CappedAtTen()
    {
        var events = new List<JobEvent>();
        for (var i = 0; i < 12; i++)
            events.Add(Ev(JobEventType.Skipped, "High", daysAgo: i, score: 80 + i));
        events.Add(Ev(JobEventType.Skipped, "Neutral"));

        var stats = ScoreboardCalculator.Compute(events, Array.Empty<ApplicationLog>(), 45, NowLocal);

        Assert.Equal(12, stats.GhostsDodged);
        Assert.Equal(10, stats.RecentDodges.Count);
        Assert.Equal(80, stats.RecentDodges[0].RiskScore);          // newest (daysAgo 0) first
        Assert.Equal("High", stats.RecentDodges[0].RiskBand);
        Assert.Equal("Acme", stats.RecentDodges[0].Company);
        Assert.Equal("Closed on the company ATS", stats.RecentDodges[0].Evidence);
        Assert.True(stats.RecentDodges[0].OccurredAtUtc > stats.RecentDodges[9].OccurredAtUtc);
    }
}
