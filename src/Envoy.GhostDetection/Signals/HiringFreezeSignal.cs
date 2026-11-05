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
            var stateMatch = StateMatch.Unknown;
            foreach (var notice in notices)
            {
                var since = (reference - notice.NoticeDate).TotalDays;
                if (since < 0 || since > RecentWindowDays)
                    continue; // only recent notices fire; stale ones are dropped

                if (DomScorer.NormalizedSimilarity(normalizedCompany, NormalizeEmployer(notice.Employer)) < EmployerNameMatch)
                    continue;

                var matchState = SameState(posting.Location, notice);
                if (matchState == StateMatch.Mismatch)
                    continue; // clearly different state: too weak to fire

                matches.Add(notice);
                if (matchState == StateMatch.Confirmed)
                    stateMatch = StateMatch.Confirmed;
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

    private static readonly string[] LegalSuffixes =
    {
        "incorporated", "inc", "llc", "llp", "lp", "plc", "ltd",
        "corp", "corporation", "company", "co", "limited", "holdings", "group"
    };

    private static string NormalizeEmployer(string? employer)
    {
        if (string.IsNullOrWhiteSpace(employer))
            return string.Empty;

        var sb = new System.Text.StringBuilder(employer.Length);
        foreach (var ch in employer)
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');

        var tokens = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(tokens.Length);
        var dbaIndex = Array.IndexOf(tokens, "dba");
        var take = dbaIndex >= 0 ? dbaIndex : tokens.Length;

        for (var i = 0; i < take; i++)
        {
            // A trailing legal-suffix token is dropped ("...USA, LLC." -> "...usa").
            if (i == take - 1 && LegalSuffixes.Contains(tokens[i]))
                continue;
            kept.Add(tokens[i]);
        }

        return string.Join(' ', kept);
    }

    // ── State matching ─────────────────────────────────────────────────
    // WARN is employer-level, but a notice in a different state than a clearly
    // located posting is weak enough to drop. Unknown or ambiguous locations
    // stay in play (the evidence then flags the caution).

    private enum StateMatch
    {
        Confirmed,   // posting location clearly sits in the notice's state
        Mismatch,    // posting location clearly sits in a different state
        Unknown      // no / ambiguous location — employer-level match only
    }

    private static StateMatch SameState(string? postingLocation, WarnNotice notice)
    {
        var noticeState = notice.State?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(noticeState))
            return StateMatch.Unknown;
        if (string.IsNullOrWhiteSpace(postingLocation))
            return StateMatch.Unknown;

        var loc = postingLocation.ToLowerInvariant();

        var segments = postingLocation.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var trailingCode = segments.Length > 0 ? segments[^1].Trim().ToUpperInvariant() : string.Empty;

        // "Redmond, WA" and the notice is a WA notice
        if (trailingCode.Length == 2 && trailingCode == noticeState)
            return StateMatch.Confirmed;

        // A full state name in the location, e.g. "Interlake Ave N, Washington"
        if (StateNames.TryGetValue(noticeState, out var stateName) &&
            loc.Contains(stateName, StringComparison.OrdinalIgnoreCase))
            return StateMatch.Confirmed;

        // The notice's city/town appearing in the location, e.g. notice "Redmond"
        if (!string.IsNullOrWhiteSpace(notice.Location))
        {
            var noticeCity = notice.Location.Split(',')[0].Trim();
            if (noticeCity.Length >= 4 && loc.Contains(noticeCity, StringComparison.OrdinalIgnoreCase))
                return StateMatch.Confirmed;
        }

        // Scan for any other full state name in the location, e.g. "Tacoma,
        // Washington" where the notice was filed in California.
        foreach (var (code, name) in StateNames)
        {
            if (code != noticeState && loc.Contains(name, StringComparison.OrdinalIgnoreCase))
                return StateMatch.Mismatch;
        }

        // "Redmond, OR" where the notice was filed in WA — different state.
        if (trailingCode.Length == 2 && StateNames.ContainsKey(trailingCode) && trailingCode != noticeState)
            return StateMatch.Mismatch;

        // No usable state signal in the location → employer-level match.
        return StateMatch.Unknown;
    }

    private static readonly Dictionary<string, string> StateNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = "alabama", ["AK"] = "alaska", ["AZ"] = "arizona", ["AR"] = "arkansas",
        ["CA"] = "california", ["CO"] = "colorado", ["CT"] = "connecticut", ["DE"] = "delaware",
        ["FL"] = "florida", ["GA"] = "georgia", ["HI"] = "hawaii", ["ID"] = "idaho",
        ["IL"] = "illinois", ["IN"] = "indiana", ["IA"] = "iowa", ["KS"] = "kansas",
        ["KY"] = "kentucky", ["LA"] = "louisiana", ["ME"] = "maine", ["MD"] = "maryland",
        ["MA"] = "massachusetts", ["MI"] = "michigan", ["MN"] = "minnesota", ["MS"] = "mississippi",
        ["MO"] = "missouri", ["MT"] = "montana", ["NE"] = "nebraska", ["NV"] = "nevada",
        ["NH"] = "new hampshire", ["NJ"] = "new jersey", ["NM"] = "new mexico", ["NY"] = "new york",
        ["NC"] = "north carolina", ["ND"] = "north dakota", ["OH"] = "ohio", ["OK"] = "oklahoma",
        ["OR"] = "oregon", ["PA"] = "pennsylvania", ["RI"] = "rhode island", ["SC"] = "south carolina",
        ["SD"] = "south dakota", ["TN"] = "tennessee", ["TX"] = "texas", ["UT"] = "utah",
        ["VT"] = "vermont", ["VA"] = "virginia", ["WA"] = "washington", ["WV"] = "west virginia",
        ["WI"] = "wisconsin", ["WY"] = "wyoming", ["DC"] = "district of columbia"
    };

    // ── Scoring ────────────────────────────────────────────────────────

    private static (double Score, double Confidence) ComputeScoreAndConfidence(
        List<WarnNotice> matches, WarnNotice newest, DateTime reference, StateMatch stateMatch)
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
        if (stateMatch == StateMatch.Confirmed)
            confidence += 0.10;

        // Several notices in the window make the freeze picture solid.
        if (matches.Count > 1)
            confidence += Math.Min(0.10, 0.05 * (matches.Count - 1));

        confidence = Math.Min(0.80, confidence);
        return (score, Math.Round(confidence, 2));
    }

    // ── Evidence ───────────────────────────────────────────────────────

    private static string[] BuildEvidence(
        List<WarnNotice> matches, WarnNotice newest, StateMatch stateMatch)
    {
        var workers = newest.AffectedCount > 0
            ? $" for {newest.AffectedCount} workers"
            : string.Empty;

        var noticeState = newest.State?.Trim().ToUpperInvariant() ?? string.Empty;
        var place = !string.IsNullOrWhiteSpace(newest.Location)
            ? newest.Location.Trim()
            : (StateNames.TryGetValue(noticeState, out var stateName) ? stateName : noticeState);

        var lines = new List<string>
        {
            $"{newest.Employer} filed a WARN notice{workers}{PlacePhrase(place)} on {newest.NoticeDate:MMMM d, yyyy}. A role posted after that may be a backfill, a different team, or a listing left up."
        };

        if (matches.Count > 1)
            lines.Add($"{matches.Count} WARN notices from this employer fall within the last {RecentWindowDays} days, which strengthens the correlation.");

        if (stateMatch == StateMatch.Unknown)
            lines.Add("The posting's location could not be compared with the state on the notice, so treat this as employer-level evidence.");

        lines.Add("A WARN notice does not mean this specific role was cut — employers often keep hiring in other divisions.");

        return lines.Take(3).ToArray();
    }

    private static string PlacePhrase(string place) =>
        string.IsNullOrWhiteSpace(place) ? string.Empty : $" in {place}";
}
