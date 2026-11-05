using Envoy.Core.Models;
using Envoy.Core.Services;
using Xunit;

namespace Envoy.Core.Tests;

/// <summary>
/// APP-14: verified-delivery math on the scoreboard. The verified number
/// counts only submissions whose post-submit verification actually passed;
/// unverified completions stay in their own bucket — honest stats or no stats.
/// </summary>
public sealed class VerifiedDeliveryStatsTests
{
    private static ApplicationLog Completed(DateTime? completedAt, bool? verified, ApplicationStatus? statusOverride = null) => new()
    {
        JobUrl = "https://x.com/1",
        Company = "c",
        JobTitle = "t",
        Status = statusOverride ?? ApplicationStatus.Completed,
        StartedAt = (completedAt ?? DateTime.UtcNow).AddMinutes(-2),
        CompletedAt = completedAt,
        VerifiedSubmission = verified,
        VerificationNote = verified == true ? "url contains confirmation" : "unverified"
    };

    private static readonly DateTime NowLocal = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly JobEvent[] NoEvents = Array.Empty<JobEvent>();

    [Fact]
    public void Buckets_StayDistinct()
    {
        var submits = new[]
        {
            Completed(NowLocal.AddDays(-1), verified: true),
            Completed(NowLocal.AddHours(-2), verified: true),
            Completed(NowLocal.AddHours(-1), verified: false),
            Completed(NowLocal.AddMinutes(-1), verified: null),
            new ApplicationLog { Status = ApplicationStatus.Failed, VerifiedSubmission = null }
        };

        var stats = ScoreboardCalculator.Compute(NoEvents, submits, 45, NowLocal);

        Assert.Equal(2, stats.SubmissionsVerified);
        Assert.Equal(2, stats.SubmissionsUnverified);
        Assert.Equal(2, stats.SubmissionsThisWeek);
        Assert.Equal(2, stats.SubmissionsVerified + stats.SubmissionsUnverified - 2); // nothing double-counted
        Assert.Equal(2, stats.VerifiedPerDay.Count);
    }

    [Fact]
    public void Unverified_NeverFoldsIntoVerified()
    {
        var submits = new[]
        {
            Completed(NowLocal.AddHours(-1), verified: null),
            Completed(NowLocal.AddHours(-2), verified: false)
        };

        var stats = ScoreboardCalculator.Compute(NoEvents, submits, 45, NowLocal);

        Assert.Equal(0, stats.SubmissionsVerified);
        Assert.Equal(2, stats.SubmissionsUnverified);
        Assert.Empty(stats.VerifiedPerDay);
    }

    [Fact]
    public void SevenDayWindow_DoesNotCountOldSubmissions()
    {
        var submits = new[]
        {
            Completed(NowLocal.AddDays(-3), verified: true),
            Completed(NowLocal.AddDays(-30), verified: true)
        };

        var stats = ScoreboardCalculator.Compute(NoEvents, submits, 45, NowLocal);

        Assert.Equal(2, stats.SubmissionsVerified);
        Assert.Equal(1, stats.SubmissionsThisWeek);
        var perDay = stats.VerifiedPerDay;
        Assert.Equal(2, perDay.Count);
        Assert.All(perDay, d => Assert.True(d.Count >= 1));
    }

    [Fact]
    public void FailedCaptchas_AndDuplicates_AreNotSubmissions()
    {
        var submits = new[]
        {
            Completed(NowLocal.AddHours(-1), verified: true),
            new ApplicationLog { Status = ApplicationStatus.RequiresCaptcha, VerifiedSubmission = null },
            new ApplicationLog { Status = ApplicationStatus.DuplicateSkipped, VerifiedSubmission = null }
        };

        var stats = ScoreboardCalculator.Compute(NoEvents, submits, 45, NowLocal);

        Assert.Equal(1, stats.SubmissionsVerified);
        Assert.Equal(0, stats.SubmissionsUnverified);
        Assert.Single(stats.VerifiedPerDay);
    }

    [Fact]
    public void EmptyHistory_ZerosEverywhere()
    {
        var stats = ScoreboardCalculator.Compute(NoEvents, Array.Empty<ApplicationLog>(), 45, NowLocal);

        Assert.Equal(0, stats.SubmissionsVerified);
        Assert.Equal(0, stats.SubmissionsUnverified);
        Assert.Equal(0, stats.SubmissionsThisWeek);
        Assert.Empty(stats.VerifiedPerDay);
    }
}
