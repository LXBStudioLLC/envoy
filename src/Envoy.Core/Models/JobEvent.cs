namespace Envoy.Core.Models;

/// <summary>
/// One row in the append-only activity ledger behind the scoreboard. Every
/// decision the user makes about a posting (and, later, every scored sighting)
/// is recorded with the ghost-risk evidence that was on screen at the time, so
/// stats like "ghosts dodged" are backed by receipts instead of invented
/// numbers. The same table is the cross-session listing history the
/// repost-frequency signal needs for its stronger detection path.
/// </summary>
public class JobEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public JobEventType Type { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    // Identity snapshot of the posting. Postings are transient upstream (the
    // discovery cache is in-memory only), so each event carries its own copy.
    public string JobUrl { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;

    /// <summary>Stable cross-session identity from <see cref="Services.PostingKey"/>.</summary>
    public string PostingKey { get; set; } = string.Empty;

    /// <summary>Feed the posting came from (e.g. "Greenhouse"). Empty when unknown.</summary>
    public string Source { get; set; } = string.Empty;

    // Ghost-risk snapshot at the moment of the event; null when the posting was
    // never scored. The band is text ("Neutral"/"Elevated"/"High") so stored
    // rows can't be re-labeled by a later renumbering of the source enum.
    public double? RiskScore { get; set; }
    public string? RiskBand { get; set; }
    public string? Evidence { get; set; }

    /// <summary>Links Applied/Declined events back to their submit log.</summary>
    public Guid? ApplicationLogId { get; set; }

    /// <summary>
    /// Builds a ledger event for a posting the user saw or acted on in a
    /// results list. The score snapshot is whatever was on screen at the time;
    /// null means the posting was never scored.
    /// </summary>
    public static JobEvent ForPosting(
        JobEventType type,
        string jobUrl,
        string jobTitle,
        string company,
        string source,
        GhostScoreSnapshot? ghostScore) => new()
    {
        Type = type,
        JobUrl = jobUrl,
        JobTitle = jobTitle,
        Company = company,
        Source = source,
        PostingKey = Services.PostingKey.For(jobUrl, company, jobTitle),
        RiskScore = ghostScore?.RiskScore,
        RiskBand = ghostScore?.Band,
        Evidence = ghostScore == null || ghostScore.TopEvidence.Length == 0
            ? null
            : string.Join("\n", ghostScore.TopEvidence)
    };

    /// <summary>
    /// Maps a finished submit-flow log to its ledger event: a completed submit
    /// becomes <see cref="JobEventType.Applied"/>, a user cancel at the gate
    /// becomes <see cref="JobEventType.Declined"/>. Every other terminal status
    /// (failure, CAPTCHA, safety halt) is a machine outcome, not a user
    /// decision, and produces no event.
    /// </summary>
    public static JobEvent? FromApplication(ApplicationLog log, GhostScoreSnapshot? ghostScore)
    {
        JobEventType type;
        switch (log.Status)
        {
            case ApplicationStatus.Completed: type = JobEventType.Applied; break;
            case ApplicationStatus.DeclinedByUser: type = JobEventType.Declined; break;
            default: return null;
        }

        var jobEvent = ForPosting(type, log.JobUrl, log.JobTitle, log.Company, source: "", ghostScore);
        jobEvent.ApplicationLogId = log.Id;
        return jobEvent;
    }
}

/// <summary>
/// What happened. Values are persisted as integers in envoy.db — append new
/// members at the end, never reorder.
/// </summary>
public enum JobEventType
{
    /// <summary>Posting was scored and shown to the user in a results list.</summary>
    Sighted,

    /// <summary>User opened the posting to look at it.</summary>
    Viewed,

    /// <summary>User explicitly passed on the posting from a results list.</summary>
    Skipped,

    /// <summary>User reviewed the filled application and cancelled at the submit gate.</summary>
    Declined,

    /// <summary>Application was submitted.</summary>
    Applied
}
