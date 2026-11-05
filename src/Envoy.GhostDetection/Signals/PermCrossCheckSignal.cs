using Envoy.Core.Services;
using Envoy.GhostDetection.Models;
using Envoy.GhostDetection.Services;
using System.Text.Json;

namespace Envoy.GhostDetection.Signals;

/// <summary>
/// Probabilistic signal: cross-check a posting against the local, pre-shrunk
/// index of recent DOL PERM labor-certification filings.
///
/// <para>Under PERM, an employer sponsoring a foreign worker for a green card
/// must publicly advertise the role to test the labor market. A live posting
/// matching a recent filing may be a legally required ad for a role that is
/// already effectively filled. This is correlational, not proof: large
/// employers file many PERMs and also hire real people for the same titles.
/// The signal therefore stays at a low score with moderate confidence and is
/// one line of evidence, never a verdict.</para>
///
/// <para>Data: local only (<see cref="PermIndex"/>). The bundled pre-shrunk
/// index covers filings received in the last ~12 months; the raw OFLC files
/// (hundreds of MB) are never shipped, downloaded, or parsed per-posting.
/// <see cref="RequiresNetwork"/> is false.</para>
///
/// <para>Precision rules: fires only on a strong employer-name match (≥ 0.85)
/// AND a similar job title; a bare employer-name match never fires on its
/// own, since large employers file constantly.</para>
/// </summary>
public class PermCrossCheckSignal : IGhostSignal
{
    public string Name => "PERM Filing Cross-Check";
    public SignalTier Tier => SignalTier.Probabilistic;
    public bool RequiresNetwork => false;

    private const double EmployerNameMatch = 0.85; // same bar as HiringFreezeSignal/DuplicateJdSignal

    // Job-title match: Levenshtein ≥ 0.80 on the trimmed strings, or ≥ 0.70
    // shared-token containment (so "Senior Software Engineer" still matches a
    // filing for "Software Engineer, Senior" via tokens).
    private const double TitleLevenshteinMatch = 0.80;
    private const double TitleTokenContainmentMatch = 0.70;

    private const double Score = 0.35;
    private const double Confidence = 0.60;

    public Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(posting.CompanyName))
                return Task.FromResult<SignalResult?>(null);

            var filings = GetFilings(posting);
            if (filings.Count == 0)
                return Task.FromResult<SignalResult?>(null);

            var normalizedCompany = EmployerNormalizer.Normalize(posting.CompanyName);
            if (normalizedCompany.Length == 0)
                return Task.FromResult<SignalResult?>(null);

            // Only filings from the last 12 months count as "recent"; older
            // rows are filtered defensively in case the index is stale.
            var receivedFrom = DateTime.UtcNow.AddMonths(-12);

            var matches = new List<PermFiling>();
            foreach (var filing in filings)
            {
                if (filing.ReceivedDate < receivedFrom)
                    continue;

                if (DomScorer.NormalizedSimilarity(normalizedCompany, EmployerNormalizer.Normalize(filing.Employer)) < EmployerNameMatch)
                    continue;

                var matchState = LocationStateMatcher.Match(posting.Location, filing.State);
                if (matchState == SignalStateMatch.Mismatch)
                    continue; // clearly different state: too weak to fire

                if (!TitleSimilar(posting.JobTitle, filing.JobTitle))
                    continue;

                matches.Add(filing);
            }

            if (matches.Count == 0)
                return Task.FromResult<SignalResult?>(null);

            var newest = matches.Aggregate((a, b) => a.ReceivedDate > b.ReceivedDate ? a : b);

            return Task.FromResult<SignalResult?>(new SignalResult
            {
                SignalName = Name,
                Score = Score,
                Confidence = Confidence,
                Evidence = BuildEvidence(matches, newest, posting.JobTitle),
                Tier = Tier
            });
        }
        catch
        {
            return Task.FromResult<SignalResult?>(null);
        }
    }

    // ── Index access ───────────────────────────────────────────────────
    // Like DuplicateJdSignal's dupcheck.corpus and HiringFreezeSignal's
    // warn.dataset, a caller may pass filings inline via Extra["perm.dataset"]
    // (tests, batch scoring); otherwise the shared bundled/cached index is used.

    private static IReadOnlyList<PermFiling> GetFilings(JobPosting posting)
    {
        if (posting.Extra.TryGetValue("perm.dataset", out var inlineJson) &&
            !string.IsNullOrWhiteSpace(inlineJson))
        {
            var inline = JsonSerializer.Deserialize<List<PermFiling>>(inlineJson);
            if (inline is { Count: > 0 })
                return inline;
        }

        return PermIndex.Load();
    }

    // ── Title similarity ───────────────────────────────────────────────

    private static bool TitleSimilar(string? postingTitle, string filingTitle)
    {
        if (string.IsNullOrWhiteSpace(postingTitle) || string.IsNullOrWhiteSpace(filingTitle))
            return false;

        var a = postingTitle.Trim().ToLowerInvariant();
        var b = filingTitle.Trim().ToLowerInvariant();

        if (DomScorer.NormalizedSimilarity(a, b) >= TitleLevenshteinMatch)
            return true;

        var tokensA = Tokenize(a).ToHashSet();
        var tokensB = Tokenize(b).ToHashSet();
        if (tokensA.Count == 0 || tokensB.Count == 0)
            return false;

        int shared = tokensA.Intersect(tokensB).Count();
        var containment = (double)shared / Math.Min(tokensA.Count, tokensB.Count);
        return containment >= TitleTokenContainmentMatch;
    }

    private static IEnumerable<string> Tokenize(string s) =>
        s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
         .Select(t => t.Trim('.', ',', '(', ')'))
         .Where(t => t.Length > 0);

    // ── Evidence ───────────────────────────────────────────────────────

    private static string[] BuildEvidence(List<PermFiling> matches, PermFiling newest, string postingTitle)
    {
        var soc = string.IsNullOrWhiteSpace(newest.SocCode) ? "" : $" ({newest.SocCode})";
        var lines = new List<string>
        {
            $"{newest.Employer} filed a PERM labor certification for a similar role ({newest.JobTitle}{soc}) in {newest.State} on {newest.ReceivedDate:MMMM d, yyyy}. Some postings that match a PERM filing are legally required ads for roles that are already filled."
        };

        if (matches.Count > 1)
            lines.Add($"{matches.Count} recent PERM filings from this employer are similar to this job title ({postingTitle}), which strengthens the correlation.");

        lines.Add("This is a correlation, not proof: many employers file PERM applications and also hire plenty of people for the same titles.");

        return lines.Take(3).ToArray();
    }
}
