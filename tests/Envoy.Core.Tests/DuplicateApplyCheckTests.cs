using Envoy.Core.Models;
using Envoy.Core.Services;
using Xunit;

namespace Envoy.Core.Tests;

/// <summary>
/// Offline tests for the duplicate-application guard (APP-04). Pure
/// computation, deterministic clock: all "today now" values are injected.
/// </summary>
public sealed class DuplicateApplyCheckTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    private static ApplicationLog CompletedLog(
        string url, string company = "Acme, Inc.", string title = "Software Engineer",
        DateTime? completedAt = null) =>
        new()
        {
            JobUrl = url,
            Company = company,
            JobTitle = title,
            Status = ApplicationStatus.Completed,
            StartedAt = (completedAt ?? Now).AddMinutes(-2),
            CompletedAt = completedAt ?? Now
        };

    [Fact]
    public void SameCanonicalUrl_PriorCompleted_Blocks()
    {
        var prior = new[] { CompletedLog("https://boards.greenhouse.io/acme/jobs/123?utm_source=x") };
        var (blocked, message) = DuplicateApplyCheck.Evaluate(
            prior, "https://boards.greenhouse.io/acme/jobs/123?ref=linkedin", "Acme Inc", "Software Engineer", 0, Now);

        Assert.True(blocked);
        Assert.Contains("already completed an application", message);
        Assert.Contains("2026-11-05", message);
    }

    [Fact]
    public void SameCompanyTitle_DifferentHost_BlocksViaFallback()
    {
        // The same role mirrored on a different ATS domain still counts.
        var prior = new[] { CompletedLog("https://job-boards.greenhouse.io/acme/jobs/456") };
        var (blocked, message) = DuplicateApplyCheck.Evaluate(
            prior, "https://boards.greenhouse.io/acme/jobs/456", "acme inc", "software engineer", 0, Now);

        Assert.True(blocked);
        Assert.Contains("already completed", message);
    }

    [Fact]
    public void DifferentPosting_SameCompany_DoesNotBlock()
    {
        var prior = new[] { CompletedLog("https://boards.greenhouse.io/acme/jobs/123") };
        var (blocked, _) = DuplicateApplyCheck.Evaluate(
            prior, "https://boards.greenhouse.io/acme/jobs/999", "Acme, Inc.", "Data Analyst", 0, Now);

        Assert.False(blocked);
    }

    [Fact]
    public void PriorIncomplete_NeverBlocks()
    {
        ApplicationLog[] prior =
        {
            new() { JobUrl = "https://boards.greenhouse.io/acme/jobs/123", Company = "Acme", JobTitle = "SWE", Status = ApplicationStatus.Failed },
            new() { JobUrl = "https://boards.greenhouse.io/acme/jobs/123", Company = "Acme", JobTitle = "SWE", Status = ApplicationStatus.RequiresCaptcha },
            new() { JobUrl = "https://boards.greenhouse.io/acme/jobs/123", Company = "Acme", JobTitle = "SWE", Status = ApplicationStatus.DeclinedByUser }
        };

        var (blocked, _) = DuplicateApplyCheck.Evaluate(
            prior, "https://boards.greenhouse.io/acme/jobs/123", "Acme", "SWE", 0, Now);

        Assert.False(blocked);
    }

    [Fact]
    public void WindowDays_Zero_BlocksForever()
    {
        var prior = new[] { CompletedLog("https://boards.greenhouse.io/acme/jobs/1", completedAt: Now.AddDays(-400)) };

        var (blocked, _) = DuplicateApplyCheck.Evaluate(
            prior, "https://boards.greenhouse.io/acme/jobs/1", "Acme", "SWE", 0, Now);

        Assert.True(blocked);
    }

    [Fact]
    public void WindowDays_PriorInsideWindow_Blocks()
    {
        var prior = new[] { CompletedLog("https://boards.greenhouse.io/acme/jobs/1", completedAt: Now.AddDays(-3)) };

        var (blocked, _) = DuplicateApplyCheck.Evaluate(
            prior, "https://boards.greenhouse.io/acme/jobs/1", "Acme", "SWE", 7, Now);

        Assert.True(blocked);
    }

    [Fact]
    public void WindowDays_PriorOlderThanWindow_Allows()
    {
        var prior = new[] { CompletedLog("https://boards.greenhouse.io/acme/jobs/1", completedAt: Now.AddDays(-31)) };

        var (blocked, message) = DuplicateApplyCheck.Evaluate(
            prior, "https://boards.greenhouse.io/acme/jobs/1", "Acme", "SWE", 30, Now);

        Assert.False(blocked);
        Assert.Equal(string.Empty, message);
    }

    [Fact]
    public void CanonicalKey_WinsOverCompanyTitleFallback()
    {
        // Two priors: an exact-key hit from long ago, and a company+title
        // match from yesterday. The strongest match (the key) is preferred
        // when both would block; the message cites the referenced posting.
        var priors = new[]
        {
            CompletedLog("https://boards.greenhouse.io/acme/jobs/123", "Other Employer", "Different Title", Now.AddDays(-90)),
            CompletedLog("https://careers.acme.com/apply/123", "Acme", "Software Engineer", Now.AddDays(-1))
        };

        var (blocked, message) = DuplicateApplyCheck.Evaluate(
            priors, "https://boards.greenhouse.io/acme/jobs/123", "Acme", "Software Engineer", 0, Now);

        Assert.True(blocked);
        // Company-title secondary match (yesterday) outranks the stale key match (90 days).
        Assert.Contains("2026-11-04", message);
    }

    [Fact]
    public void EmptyHistory_NoBlock()
    {
        var (blocked, message) = DuplicateApplyCheck.Evaluate(
            Array.Empty<ApplicationLog>(), "https://boards.greenhouse.io/acme/jobs/1", "Acme", "SWE", 0, Now);

        Assert.False(blocked);
        Assert.Equal(string.Empty, message);
    }
}
