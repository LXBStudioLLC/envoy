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

    // ── APP-05 honest-completion evidence ─────────────────────────────
    /// <summary>Required fill/upload steps in the executed template (null when no template ran).</summary>
    public int? RequiredFieldCount { get; set; }

    /// <summary>How many of those required steps the engine actually carried out.</summary>
    public int? FilledFieldCount { get; set; }

    /// <summary>Post-submit verification (APP-05): null when the template has no verify block.</summary>
    public bool? VerifiedSubmission { get; set; }

    /// <summary>What the verification (or the fill-integrity failure) actually observed.</summary>
    public string? VerificationNote { get; set; }
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
    DeclinedByUser,

    /// <summary>The duplicate guard (APP-04) stopped the submit because this posting was already applied to.
    /// An explicit user override can still proceed; the row records the blocking decision either way.</summary>
    DuplicateSkipped,

    /// <summary>Generic fill (APP-09) prepared the form; the human completes leftover fields and
    /// clicks the site's own Submit button. Envoy never presses that button in this mode.</summary>
    NeedsUserSubmit
}

public enum ExecutionMode
{
    Stealth,
    Safe
}
