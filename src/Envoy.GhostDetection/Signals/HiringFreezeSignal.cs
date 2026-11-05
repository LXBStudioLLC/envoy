using Envoy.Core.Services;
using Envoy.GhostDetection.Services;
using Envoy.GhostDetection.Models;
using System.Text.Json;

namespace Envoy.GhostDetection.Signals;

/// <summary>
/// Probabilistic signal: the employer recently filed a public WARN Act layoff
/// notice, so a posting listed now may be a backfill that never happens, or a
/// listing left up.
///
/// <para>WARN (Worker Adjustment and Retraining Notification Act) notices are
/// employer-level data with no role detail. This is a correlation, not proof:
/// a company laying off one division may genuinely be hiring in another. The
/// signal therefore fires at low-to-moderate strength, never condemns a named
/// company, and never claims the specific role was cut.</para>
///
/// <para>Precision rules: fires only on a strong normalized employer-name match
/// (≥ 0.85, the same bar <see cref="DuplicateJdSignal"/> uses); only notices
/// within the last 90 days of the posting date count; when the posting's
/// location clearly sits in a different state than the notice, it does not
/// fire.</para>
///
/// <para>The dataset is local (<see cref="WarnDataset"/> — bundled seed, or a
/// user-refreshed cache). No network calls, no scraping, no auth.</para>
/// </summary>
public class HiringFreezeSignal : IGhostSignal
{
    public string Name => "Hiring Freeze";
    public SignalTier Tier => SignalTier.Probabilistic;
    public bool RequiresNetwork => false;

    private const int RecentWindowDays = 90;

    // Strong employer-name match (normalized), the same bar DuplicateJdSignal
    // uses for company equality — a weak or partial name match must not fire.
    private const double EmployerNameMatch = 0.85;

    public Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(posting.CompanyName))
                return Task.FromResult<SignalResult?>(null);

            var notices = GetNotices(posting);
            if (notices.Count == 0)
                return Task.FromResult<SignalResult?>(null);

            var reference = posting.PostedAtUtc ?? posting.LastUpdatedUtc ?? DateTime.UtcNow;
            var normalizedCompany = NormalizeEmployer(posting.CompanyName);
            if (normalizedCompany.Length == 0)
                return Task.FromResult<SignalResult?>(null);

            var matches = new List<WarnNotice>();
            var stateMatch = SignalStateMatch.Unknown;
            foreach (var notice in notices)
            {
                var since = (reference - notice.NoticeDate).TotalDays;
                if (since < 0 || since > RecentWindowDays)
                    continue; // only recent notices fire; stale ones are dropped

                if (DomScorer.NormalizedSimilarity(normalizedCompany, NormalizeEmployer(notice.Employer)) < EmployerNameMatch)
                    continue;

                var matchState = LocationStateMatcher.Match(posting.Location, notice.State, notice.Location);
                if (matchState == SignalStateMatch.Mismatch)
                    continue; // clearly different state: too weak to fire

                matches.Add(notice);
                if (matchState == SignalStateMatch.Confirmed)
                    stateMatch = SignalStateMatch.Confirmed;
            }

            if (matches.Count == 0)
                return Task.FromResult<SignalResult?>(null);

            var newest = matches.Aggregate((a, b) => a.NoticeDate > b.NoticeDate ? a : b);
            var (score, confidence) = ComputeScoreAndConfidence(matches, newest, reference, stateMatch);

            return Task.FromResult<SignalResult?>(new SignalResult
            {
                SignalName = Name,
                Score = score,
                Confidence = confidence,
                Evidence = BuildEvidence(matches, newest, stateMatch),
                Tier = Tier
            });
        }
        catch
        {
            return Task.FromResult<SignalResult?>(null);
        }
    }

    // ── Dataset access ─────────────────────────────────────────────────
    // Like DuplicateJdSignal's dupcheck.corpus, a caller may pass the dataset
    // inline via Extra["warn.dataset"] (used by tests); otherwise the shared
    // bundled/cached dataset is used.

    private static IReadOnlyList<WarnNotice> GetNotices(JobPosting posting)
    {
        if (posting.Extra.TryGetValue("warn.dataset", out var inlineJson) &&
            !string.IsNullOrWhiteSpace(inlineJson))
        {
            var inline = JsonSerializer.Deserialize<List<WarnNotice>>(inlineJson);
            if (inline is { Count: > 0 })
                return inline;
        }

        return WarnDataset.Load();
    }

    // ── Employer-name normalization ────────────────────────────────────

    private static string NormalizeEmployer(string? employer) =>
        EmployerNormalizer.Normalize(employer);

    // ── Scoring ────────────────────────────────────────────────────────

    private static (double Score, double Confidence) ComputeScoreAndConfidence(
        List<WarnNotice> matches, WarnNotice newest, DateTime reference, SignalStateMatch stateMatch)
    {
        var daysSince = Math.Max(0, (int)(reference - newest.NoticeDate).TotalDays);

        // Fresher notice → higher (still low-to-moderate) score: 0.45 at day 0,
        // easing back to 0.30 at the 90-day edge.
        var score = 0.45 - 0.15 * (double)daysSince / RecentWindowDays;

        // Larger or company-wide notices are stronger evidence than small ones.
        if (newest.AffectedCount >= 200)
            score += 0.10;
        else if (newest.AffectedCount >= 50)
            score += 0.05;
        else if (newest.AffectedCount > 0)
            score += 0.02;

        score = Math.Min(0.60, score); // stays below deterministic territory
        score = Math.Round(score, 2);

        double confidence = 0.55;

        // A confirmed state (posting location agrees with the notice) tightens
        // the correlation.
        if (stateMatch == SignalStateMatch.Confirmed)
            confidence += 0.10;

        // Several notices in the window make the freeze picture solid.
        if (matches.Count > 1)
            confidence += Math.Min(0.10, 0.05 * (matches.Count - 1));

        confidence = Math.Min(0.80, confidence);
        return (score, Math.Round(confidence, 2));
    }

    // ── Evidence ───────────────────────────────────────────────────────

    private static string[] BuildEvidence(
        List<WarnNotice> matches, WarnNotice newest, SignalStateMatch stateMatch)
    {
        var workers = newest.AffectedCount > 0
            ? $" for {newest.AffectedCount} workers"
            : string.Empty;

        var noticeState = newest.State?.Trim().ToUpperInvariant() ?? string.Empty;
        var place = !string.IsNullOrWhiteSpace(newest.Location)
            ? newest.Location.Trim()
            : (LocationStateMatcher.StateName(noticeState) ?? noticeState);

        var lines = new List<string>
        {
            $"{newest.Employer} filed a WARN notice{workers}{PlacePhrase(place)} on {newest.NoticeDate:MMMM d, yyyy}. A role posted after that may be a backfill, a different team, or a listing left up."
        };

        if (matches.Count > 1)
            lines.Add($"{matches.Count} WARN notices from this employer fall within the last {RecentWindowDays} days, which strengthens the correlation.");

        if (stateMatch == SignalStateMatch.Unknown)
            lines.Add("The posting's location could not be compared with the state on the notice, so treat this as employer-level evidence.");

        lines.Add("A WARN notice does not mean this specific role was cut — employers often keep hiring in other divisions.");

        return lines.Take(3).ToArray();
    }

    private static string PlacePhrase(string place) =>
        string.IsNullOrWhiteSpace(place) ? string.Empty : $" in {place}";
}
