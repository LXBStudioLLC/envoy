namespace Envoy.Core.Models;

/// <summary>
/// The numbers behind the scoreboard, computed from the ledger. Every stat is
/// a point scored against the system, never a judgment of the player: dodges
/// and hours saved are the headline, submit counts appear only as context.
/// </summary>
public record ScoreboardStats(
    int GhostsDodged,
    double HoursSaved,
    int MinutesPerApplication,
    int PostingsScreened,
    int GhostsSurfaced,
    int Applications,
    int SubmitsCompleted,
    int Responses,
    double? ResponseRatePercent,
    int StreakDays,
    IReadOnlyList<DodgeReceipt> RecentDodges,

    // ── APP-14: verified-delivery stats ──────────────────────────────
    /// <summary>Submissions whose post-submit verification passed. The headline number: these demonstrably reached a live ATS.</summary>
    int SubmissionsVerified,

    /// <summary>Completed submissions where the verify block could not confirm (never counted as verified).</summary>
    int SubmissionsUnverified,

    /// <summary>Verifying or verified submissions in the last 7 local days.</summary>
    int SubmissionsThisWeek,

    /// <summary>Verified submissions per UTC day (for the weekly distribution view).</summary>
    IReadOnlyList<VerifiedSubmissionsPerDay> VerifiedPerDay,

    // ── #92: taps per application ────────────────────────────────────────
    /// <summary>Average recorded user-journey taps (queued → opened → filled →
    /// confirm) per application that finished in the last 30 days, joined by
    /// posting identity. Null until at least one application finished.</summary>
    double? TapsPerApplication);

/// <summary>
/// One dodged posting with the evidence that was on screen when the user
/// passed on it: the receipt behind the headline number.
/// </summary>
public record DodgeReceipt(
    DateTime OccurredAtUtc,
    string Company,
    string JobTitle,
    string RiskBand,
    double? RiskScore,
    string? Evidence,
    string JobUrl);

/// <summary>One UTC day bucket, for the weekly verified-submissions distribution.</summary>
public record VerifiedSubmissionsPerDay(string Day, int Count);
