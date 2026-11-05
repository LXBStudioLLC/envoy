using System.Text.RegularExpressions;
using Envoy.GhostDetection.Models;

namespace Envoy.GhostDetection.Signals;

/// <summary>
/// Weak signal: a posting in a U.S. pay-transparency jurisdiction that leaves out
/// any salary or pay range.
///
/// <para>Employers covered by these laws are generally required to publish a pay
/// range in the posting, so omitting one in a covered state or city is a small
/// red flag — often just a sloppy listing, sometimes a placeholder. Plenty of
/// real postings leave out salary, so this only ever adds a little to the
/// evidence and must never move a posting's band on its own.</para>
///
/// <para>Precision rules: fires only on a clear, conservative match on the
/// Location field (full state name or city, or a trailing two-letter state
/// code). A location like "Remote" or an uncovered state returns <c>null</c>,
/// as does any posting whose SalaryText or description already mentions pay.
/// Never a verdict on a named company.</para>
/// </summary>
public partial class SalaryAbsenceSignal : IGhostSignal
{
    public string Name => "Salary Absence";
    public SignalTier Tier => SignalTier.Weak;
    public bool RequiresNetwork => false;

    // ── Covered jurisdictions ──────────────────────────────────────────
    // U.S. states and cities whose pay-transparency laws require (or strongly
    // compel) a pay range in job postings. Keep the list easy to extend; a new
    // entry only makes the signal fire on locations it previously ignored.
    private static readonly string[] States =
    {
        "california", "colorado", "new york", "washington", "illinois",
        "hawaii", "minnesota", "maryland"
    };

    // Two-letter codes a listing may use, e.g. "San Francisco, CA".
    // Honored only as the trailing, comma-separated segment to keep the match tight.
    private static readonly string[] StateCodes = { "ca", "co", "ny", "wa", "il", "hi", "mn", "md" };

    // Cities and counties with their own posting-range ordinances.
    private static readonly string[] Cities = { "new york city", "nyc", "seattle", "jersey city", "westchester" };

    private const double Score = 0.30;      // Weak tier: low, never band-moving on its own
    private const double Confidence = 0.55; // low-to-moderate; a location is all we know

    public Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default)
    {
        try
        {
            var jurisdiction = MatchJurisdiction(posting.Location);
            if (jurisdiction == null)
                return Task.FromResult<SignalResult?>(null);

            // Salary already stated anywhere in the posting → nothing to flag.
            if (!string.IsNullOrWhiteSpace(posting.SalaryText) ||
                DescriptionMentionsPay(posting.DescriptionText))
                return Task.FromResult<SignalResult?>(null);

            var evidence = new[]
            {
                $"This posting is in {jurisdiction}, which requires a pay range, but none is listed.",
                "Some employers covered by this law leave the range off anyway; that can mean a low-effort listing, though it is not proof of anything."
            };

            return Task.FromResult<SignalResult?>(new SignalResult
            {
                SignalName = Name,
                Score = Score,
                Confidence = Confidence,
                Evidence = evidence,
                Tier = Tier
            });
        }
        catch
        {
            return Task.FromResult<SignalResult?>(null);
        }
    }

    // ── Jurisdiction matching ──────────────────────────────────────────
    // Conservative by design: only the Location field is searched (never the
    // description), and only via clear full-name or city matches or a trailing
    // two-letter state code.

    private static string? MatchJurisdiction(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return null;

        var parts = location
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim().ToLowerInvariant())
            .ToArray();

        foreach (var part in parts)
        {
            if (States.Contains(part))
                return Capitalize(part);
            if (Cities.Contains(part))
                return Capitalize(part);
        }

        // "San Francisco, CA" — a two-letter code only counts as the final segment.
        var last = parts.LastOrDefault();
        if (last != null && StateCodes.Contains(last))
            return Capitalize(StateCodeToName(last));

        return null;
    }

    // Map a trailing code back to its state name for clearer evidence text.
    private static string StateCodeToName(string code) => code switch
    {
        "ca" => "california",
        "co" => "colorado",
        "ny" => "new york",
        "wa" => "washington",
        "il" => "illinois",
        "hi" => "hawaii",
        "mn" => "minnesota",
        "md" => "maryland",
        _ => code
    };

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ── Pay detection in the description ───────────────────────────────
    // Any dollar amount, hourly/annual unit, or explicit pay phrasing counts as
    // pay being present. A bare "competitive salary" mention does too, when it
    // appears with one of these patterns — this suppression side only makes the
    // signal fire less, which is the direction we want for precision.

    [GeneratedRegex(@"\$\s*\d[\d,]*", RegexOptions.IgnoreCase)]
    private static partial Regex DollarAmount();

    [GeneratedRegex(@"\bper\s+(day|hour|week|month|year)\b|\b(hourly|daily|weekly|monthly)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PayUnit();

    private static bool DescriptionMentionsPay(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return false;

        var text = description.ToLowerInvariant();

        if (DollarAmount().IsMatch(text))
            return true;

        if (PayUnit().IsMatch(text))
            return true;

        return PayPhrases.Any(phrase => text.Contains(phrase, StringComparison.Ordinal));
    }

    // Range-specific phrasing only — a vague "competitive salary" mention does
    // NOT count as pay being stated, because the range itself is still missing.
    private static readonly string[] PayPhrases =
    {
        "salary range", "pay range", "compensation range", "wage range",
        "salary band", "pay band", "pay scale", "range of pay",
        "hourly rate", "annual salary", "base salary", "base pay"
    };
}
