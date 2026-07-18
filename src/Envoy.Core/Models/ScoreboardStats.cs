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
    IReadOnlyList<DodgeReceipt> RecentDodges);

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
