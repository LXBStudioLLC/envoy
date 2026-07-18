using Envoy.Core.Models;
using Xunit;

namespace Envoy.Core.Tests;

public class JobEventTests
{
    private static ApplicationLog Log(ApplicationStatus status) => new()
    {
        Status = status,
        JobUrl = "https://boards.greenhouse.io/acme/jobs/123",
        JobTitle = "Engineer",
        Company = "Acme"
    };

    private static readonly GhostScoreSnapshot Snapshot =
        new(72.5, "High", new[] { "Closed on the company ATS", "Live for 94 days" });

    [Fact]
    public void CompletedSubmit_BecomesAppliedEvent_WithScoreReceipts()
    {
        var log = Log(ApplicationStatus.Completed);

        var jobEvent = JobEvent.FromApplication(log, Snapshot);

        Assert.NotNull(jobEvent);
        Assert.Equal(JobEventType.Applied, jobEvent!.Type);
        Assert.Equal(log.JobUrl, jobEvent.JobUrl);
        Assert.Equal("Acme", jobEvent.Company);
        Assert.Equal("boards.greenhouse.io/acme/jobs/123", jobEvent.PostingKey);
        Assert.Equal(72.5, jobEvent.RiskScore);
        Assert.Equal("High", jobEvent.RiskBand);
        Assert.Equal("Closed on the company ATS\nLive for 94 days", jobEvent.Evidence);
        Assert.Equal(log.Id, jobEvent.ApplicationLogId);
    }

    [Fact]
    public void DeclinedAtGate_BecomesDeclinedEvent()
    {
        var jobEvent = JobEvent.FromApplication(Log(ApplicationStatus.DeclinedByUser), Snapshot);

        Assert.NotNull(jobEvent);
        Assert.Equal(JobEventType.Declined, jobEvent!.Type);
    }

    [Theory]
    [InlineData(ApplicationStatus.Pending)]
    [InlineData(ApplicationStatus.InProgress)]
    [InlineData(ApplicationStatus.Failed)]
    [InlineData(ApplicationStatus.RequiresCaptcha)]
    [InlineData(ApplicationStatus.Blocked)]
    [InlineData(ApplicationStatus.SafeModeStopped)]
    public void MachineOutcomes_ProduceNoLedgerEvent(ApplicationStatus status)
    {
        Assert.Null(JobEvent.FromApplication(Log(status), Snapshot));
    }

    [Fact]
    public void ForPosting_BuildsIdentityKeySourceAndScoreFields()
    {
        var jobEvent = JobEvent.ForPosting(
            JobEventType.Skipped,
            "https://boards.greenhouse.io/acme/jobs/123?utm_source=feed",
            "Engineer", "Acme", "Greenhouse", Snapshot);

        Assert.Equal(JobEventType.Skipped, jobEvent.Type);
        Assert.Equal("boards.greenhouse.io/acme/jobs/123", jobEvent.PostingKey);
        Assert.Equal("Greenhouse", jobEvent.Source);
        Assert.Equal(72.5, jobEvent.RiskScore);
        Assert.Equal("High", jobEvent.RiskBand);
        Assert.Null(jobEvent.ApplicationLogId);
    }

    [Fact]
    public void UnscoredPosting_LeavesRiskFieldsNull()
    {
        var jobEvent = JobEvent.FromApplication(Log(ApplicationStatus.Completed), ghostScore: null);

        Assert.NotNull(jobEvent);
        Assert.Null(jobEvent!.RiskScore);
        Assert.Null(jobEvent.RiskBand);
        Assert.Null(jobEvent.Evidence);
    }

    // Both enums are persisted as integers in envoy.db. Pin the values so a
    // reorder can't silently re-label rows that are already on disk.
    [Fact]
    public void ApplicationStatus_StoredValues_AreStable()
    {
        Assert.Equal(0, (int)ApplicationStatus.Pending);
        Assert.Equal(1, (int)ApplicationStatus.InProgress);
        Assert.Equal(2, (int)ApplicationStatus.Completed);
        Assert.Equal(3, (int)ApplicationStatus.Failed);
        Assert.Equal(4, (int)ApplicationStatus.RequiresCaptcha);
        Assert.Equal(5, (int)ApplicationStatus.Blocked);
        Assert.Equal(6, (int)ApplicationStatus.SafeModeStopped);
        Assert.Equal(7, (int)ApplicationStatus.DeclinedByUser);
    }

    [Fact]
    public void JobEventType_StoredValues_AreStable()
    {
        Assert.Equal(0, (int)JobEventType.Sighted);
        Assert.Equal(1, (int)JobEventType.Viewed);
        Assert.Equal(2, (int)JobEventType.Skipped);
        Assert.Equal(3, (int)JobEventType.Declined);
        Assert.Equal(4, (int)JobEventType.Applied);
    }
}
