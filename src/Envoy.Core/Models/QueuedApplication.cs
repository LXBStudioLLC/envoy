namespace Envoy.Core.Models;

/// <summary>
/// One posting waiting to become a submission (APP-07). The queue holds
/// everything the no-scrape prepare path needs — the description came with
/// the discovery result — so the worker can tailor an entire batch without a
/// single page navigation. Filling stays human-gated (every submit clears the
/// same gate as the single-apply flow).
/// </summary>
public class QueuedApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MasterProfileId { get; set; }

    /// <summary>Stable posting identity (PostingKey.For) — dedupe + ordering.</summary>
    public string PostingKey { get; set; } = string.Empty;
    public string JobUrl { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;

    /// <summary>The carried JD from discovery. The queue's whole point.</summary>
    public string DescriptionText { get; set; } = string.Empty;

    public double? GhostRiskScore { get; set; }
    public string? GhostRiskBand { get; set; }

    public QueueItemStatus Status { get; set; } = QueueItemStatus.Queued;

    /// <summary>Honest per-status note (why it failed, the duplicate's date, captcha hint…).</summary>
    public string? Note { get; set; }

    /// <summary>Set once the prepare phase produced a TailoredProfile.</summary>
    public Guid? TailoredProfileId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

// Persisted as integers in envoy.db — append new members at the end, never reorder.
public enum QueueItemStatus
{
    /// <summary>Waiting for the worker.</summary>
    Queued,

    /// <summary>Worker is tailoring this item right now.</summary>
    Preparing,

    /// <summary>Tailored + PDF ready; waiting for the user's one-click fill.</summary>
    AppReady,

    /// <summary>The fill completed and was verified.</summary>
    Submitted,

    /// <summary>Prepare or fill failed; the note says why. Retryable.</summary>
    Failed,

    /// <summary>Removed by the user before it ran.</summary>
    Skipped,

    /// <summary>The duplicate guard found a completed application for this posting.</summary>
    BlockedDuplicate,

    /// <summary>The page hands off to the user for a captcha; resume when solved.</summary>
    NeedsCaptcha
}
