using Envoy.Core.Services;
using Envoy.GhostDetection.Models;
using System.Text.Json;

namespace Envoy.GhostDetection.Signals;

/// <summary>
/// Weak signal: detect near-identical job descriptions posted by unrelated companies.
///
/// <para>Algorithm:
/// 1. Normalize text (lowercase, replace non-alnum with space, collapse whitespace, tokenize).
/// 2. Build 5-word shingles (sliding window over normalized tokens).
/// 3. Compare against a caller-supplied corpus (<c>Extra["dupcheck.corpus"]</c>) using
///    containment: <c>|intersection| / min(|shingles|)</c>.
/// 4. Only flag when the matching corpus entry is from a different company (same-company
///    reposting is handled by <see cref="RepostFrequencySignal"/>).
/// </para>
///
/// <para>Precision rules: texts below 400 chars / 60 tokens are ignored; malformed JSON
/// is swallowed (returns null); only the first 50 corpus entries are processed.
/// </para>
/// </summary>
public class DuplicateJdSignal : IGhostSignal
{
    public string Name => "Duplicate JD";
    public SignalTier Tier => SignalTier.Weak;
    public bool RequiresNetwork => false;

    private const double ContainmentThreshold = 0.65;
    private static readonly JsonSerializerOptions CorpusJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default)
    {
        try
        {
            var postingTokens = NormalizeAndTokenize(posting.DescriptionText);
            if (postingTokens == null || postingTokens.Length < 60)
                return Task.FromResult<SignalResult?>(null);

            var postingShingles = BuildShingles(postingTokens);
            if (postingShingles.Count == 0)
                return Task.FromResult<SignalResult?>(null);

            var corpusJson = posting.Extra.TryGetValue("dupcheck.corpus", out var corpusValue)
                ? corpusValue
                : null;

            if (string.IsNullOrWhiteSpace(corpusJson))
                return Task.FromResult<SignalResult?>(null);

            List<CorpusEntry> corpusEntries;
            try
            {
                corpusEntries = JsonSerializer.Deserialize<List<CorpusEntry>>(corpusJson, CorpusJsonOptions) ?? new List<CorpusEntry>();
            }
            catch (JsonException)
            {
                return Task.FromResult<SignalResult?>(null);
            }

            if (corpusEntries.Count == 0)
                return Task.FromResult<SignalResult?>(null);

            var matches = new List<MatchInfo>();
            var maxCorpusEntries = Math.Min(corpusEntries.Count, 50);

            for (int i = 0; i < maxCorpusEntries; i++)
            {
                var entry = corpusEntries[i];
                if (string.IsNullOrWhiteSpace(entry.Company) || string.IsNullOrWhiteSpace(entry.Description))
                    continue;

                if (IsSameCompany(posting.CompanyName, entry.Company))
                    continue;

                var entryTokens = NormalizeAndTokenize(entry.Description);
                if (entryTokens == null || entryTokens.Length < 60)
                    continue;

                var entryShingles = BuildShingles(entryTokens);
                if (entryShingles.Count == 0)
                    continue;

                var containment = ComputeContainment(postingShingles, entryShingles);
                if (containment >= ContainmentThreshold)
                {
                    matches.Add(new MatchInfo(entry.Company, containment));
                }
            }

            if (matches.Count == 0)
                return Task.FromResult<SignalResult?>(null);

            var (score, confidence) = ComputeScoreAndConfidence(matches);
            var evidence = BuildEvidence(matches);

            return Task.FromResult<SignalResult?>(new SignalResult
            {
                SignalName = Name,
                Score = score,
                Confidence = confidence,
                Evidence = evidence,
                Tier = Tier
            });
        }
        catch
        {
            return Task.FromResult<SignalResult?>(null);
        }
    }

    // ── Text normalization ───────────────────────────────────────────

    private static string[]? NormalizeAndTokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 400)
            return null;

        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
            else
                sb.Append(' ');
        }

        var tokens = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length >= 60 ? tokens : null;
    }

    // ── Shingling ──────────────────────────────────────────────────────

    private static HashSet<string> BuildShingles(string[] tokens)
    {
        var shingles = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i <= tokens.Length - 5; i++)
        {
            shingles.Add(string.Join(" ", tokens[i..(i + 5)]));
        }
        return shingles;
    }

    // ── Containment ────────────────────────────────────────────────────

    private static double ComputeContainment(HashSet<string> postingShingles, HashSet<string> entryShingles)
    {
        int intersectionCount = 0;
        foreach (var s in postingShingles)
        {
            if (entryShingles.Contains(s))
                intersectionCount++;
        }

        var minSize = Math.Min(postingShingles.Count, entryShingles.Count);
        if (minSize == 0)
            return 0;

        return (double)intersectionCount / minSize;
    }

    // ── Company guard ──────────────────────────────────────────────────

    private static bool IsSameCompany(string postingCompany, string entryCompany)
    {
        if (string.IsNullOrWhiteSpace(postingCompany) || string.IsNullOrWhiteSpace(entryCompany))
            return true;

        if (string.Equals(postingCompany, entryCompany, StringComparison.OrdinalIgnoreCase))
            return true;

        var sim = DomScorer.NormalizedSimilarity(postingCompany, entryCompany);
        return sim >= 0.85;
    }

    // ── Scoring ────────────────────────────────────────────────────────

    private static (double Score, double Confidence) ComputeScoreAndConfidence(List<MatchInfo> matches)
    {
        var bestC = matches.Max(m => m.Containment);

        double score;
        if (bestC <= ContainmentThreshold)
            score = 0.40;
        else if (bestC <= 0.80)
            score = 0.40 + (bestC - ContainmentThreshold) / 0.15 * 0.20;
        else if (bestC <= 0.90)
            score = 0.60 + (bestC - 0.80) / 0.10 * 0.18;
        else
            score = 0.78 + (bestC - 0.90) / 0.10 * 0.12;

        score = Math.Min(0.90, score);
        score = Math.Round(score, 2);

        double confidence = matches.Count switch
        {
            1 => 0.55,
            2 => 0.70,
            _ => 0.80
        };

        return (score, Math.Round(confidence, 2));
    }

    // ── Evidence ───────────────────────────────────────────────────────

    private static string[] BuildEvidence(List<MatchInfo> matches)
    {
        var best = matches.OrderByDescending(m => m.Containment).First();
        var pct = (int)(best.Containment * 100);

        var lines = new List<string>
        {
            $"This job description is a {pct}% textual match with a posting from a different company ('{best.Company}'), which can indicate a recycled or templated listing."
        };

        if (matches.Count > 1)
        {
            lines.Add($"In total, {matches.Count} postings from unrelated companies share most of this description's text.");
        }

        return lines.Take(3).ToArray();
    }

    // ── Internal models ──────────────────────────────────────────────────

    private sealed class CorpusEntry
    {
        public string Company { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    private sealed record MatchInfo(string Company, double Containment);
}
