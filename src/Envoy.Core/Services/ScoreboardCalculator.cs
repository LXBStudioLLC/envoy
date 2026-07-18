using Envoy.Core.Models;

namespace Envoy.Core.Services;

/// <summary>
/// Pure computation from ledger events to scoreboard stats. A dodge is an
/// explicit act: a skip or a gate decline on a posting that was flagged
/// Elevated or High when the user saw it. A flagged posting merely appearing
/// in results never counts — inflated numbers read as fake, and the board's
/// credibility is the product.
/// </summary>
public static class ScoreboardCalculator
{
    private const string BandElevated = "Elevated";
    private const string BandHigh = "High";
    private const int DefaultMinutesPerApplication = 45;
    private const int MaxReceipts = 10;

    // Below this many completed submits a percentage is noise, not a stat.
    private const int MinSubmitsForRate = 5;

    public static ScoreboardStats Compute(
        IReadOnlyList<JobEvent> events,
        IReadOnlyList<ApplicationLog> submits,
        int minutesPerApplication,
        DateTime nowLocal)
    {
        if (minutesPerApplication <= 0)
            minutesPerApplication = DefaultMinutesPerApplication;

        var dodges = events
            .Where(e => (e.Type == JobEventType.Skipped || e.Type == JobEventType.Declined) && IsFlagged(e))
            .OrderByDescending(e => e.OccurredAt)
            .ToList();

        var receipts = dodges
            .Take(MaxReceipts)
            .Select(e => new DodgeReceipt(
                e.OccurredAt, e.Company, e.JobTitle, e.RiskBand ?? "", e.RiskScore, e.Evidence, e.JobUrl))
            .ToList();

        // A rejection counts as a response: the stat measures whether anyone
        // answered at all, and a no is still an answer. Silence is the zero.
        var completed = submits.Count(s => s.Status == ApplicationStatus.Completed);
        var responses = submits.Count(s => s.Status == ApplicationStatus.Completed
                                           && s.Outcome != ResponseOutcome.None);

        return new ScoreboardStats(
            GhostsDodged: dodges.Count,
            HoursSaved: Math.Round(dodges.Count * minutesPerApplication / 60.0, 1),
            MinutesPerApplication: minutesPerApplication,
            PostingsScreened: events.Count(e => e.Type == JobEventType.Sighted),
            GhostsSurfaced: events.Count(e => e.Type == JobEventType.Sighted && IsFlagged(e)),
            Applications: events.Count(e => e.Type == JobEventType.Applied),
            SubmitsCompleted: completed,
            Responses: responses,
            ResponseRatePercent: completed >= MinSubmitsForRate
                ? Math.Round(100.0 * responses / completed)
                : null,
            StreakDays: ComputeStreak(events, nowLocal),
            RecentDodges: receipts);
    }

    private static bool IsFlagged(JobEvent e) => e.RiskBand is BandElevated or BandHigh;

    // Consecutive local calendar days with any ledger activity, counting back
    // from today. A streak whose last active day was yesterday still counts —
    // today isn't over, so it hasn't been broken yet.
    private static int ComputeStreak(IReadOnlyList<JobEvent> events, DateTime nowLocal)
    {
        if (events.Count == 0) return 0;
        var activeDays = events.Select(e => e.OccurredAt.ToLocalTime().Date).ToHashSet();

        var day = nowLocal.Date;
        if (!activeDays.Contains(day))
        {
            day = day.AddDays(-1);
            if (!activeDays.Contains(day)) return 0;
        }

        var streak = 0;
        while (activeDays.Contains(day))
        {
            streak++;
            day = day.AddDays(-1);
        }
        return streak;
    }
}
