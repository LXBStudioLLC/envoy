namespace Envoy.Core.Models;

public class ApplicationLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TailoredProfileId { get; set; }
    public string JobUrl { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string SiteTemplateId { get; set; } = string.Empty;
    public ApplicationStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public byte[]? BeforeScreenshot { get; set; }
    public byte[]? AfterScreenshot { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public ExecutionMode Mode { get; set; }

    // Ghost-risk snapshot at the moment of submit; null when the posting was
    // never scored. Band is text so stored rows survive enum renumbering.
    public double? GhostRiskScore { get; set; }
    public string? GhostRiskBand { get; set; }

    /// <summary>What came back from the employer, logged by the user. Feeds the response rate.</summary>
    public ResponseOutcome Outcome { get; set; } = ResponseOutcome.None;
}

// Persisted as integers in envoy.db. Append new members at the end, never reorder.
public enum ResponseOutcome
{
    /// <summary>Nothing yet. The default, and sadly the most common.</summary>
    None,
    Replied,
    Interview,
    Offer,
    Rejected
}

// Persisted as integers in envoy.db — append new members at the end, never reorder.
public enum ApplicationStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    RequiresCaptcha,
    Blocked,
    SafeModeStopped,

    /// <summary>The user reviewed the filled application at the submit gate and chose not to send it.</summary>
    DeclinedByUser
}

public enum ExecutionMode
{
    Stealth,
    Safe
}
