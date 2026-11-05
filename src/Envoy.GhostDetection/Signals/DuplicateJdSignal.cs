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
/// <para>Adaptive path (added, only when the shingle path finds nothing): a
/// corpus-learned, IDF-weighted token-overlap comparison catches copies that
/// were <b>reordered or lightly reworded</b> — same distinctive wording,
/// different sentence order, so the 5-gram shingles all break. Token weights
/// come from the batch itself: a token rare in the batch weighs much more than
/// a token every posting shares ("candidate", "experience"), which keeps
/// generic-boilerplate overlap from firing. It is gated on a minimum corpus
/// size because a one-or-two-entry batch has no statistics to learn from —
/// precision first: no statistics, no fire.</para>
///
/// <para>Precision rules: texts below 400 chars / 60 tokens are ignored; malformed JSON
/// is swallowed (returns null); only the first 50 corpus entries are processed.</para>
/// </summary>
public class DuplicateJdSignal : IGhostSignal
{
    public string Name => "Duplicate JD";
    public SignalTier Tier => SignalTier.Weak;
    public bool RequiresNetwork => false;

    private const double ContainmentThreshold = 0.65;

    // Adaptive (weighted-token) path tuning. Fires only on strong weighted
    // overlap backed by enough raw token agreement — not on a handful of rare
    // tokens among hundreds of generic ones.
    private const double WeightedThreshold = 0.70;
    private const double MinDistinctOverlap = 0.35;
    private const double MinRareMassShare = 0.60;
    private const int AdaptiveMinCorpusEntries = 8;

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

            var maxCorpusEntries = Math.Min(corpusEntries.Count, 50);

            // Tokenize every usable corpus entry once; both paths consume it.
            // The array keeps word order (shingles); the set powers the weighted path.
            var entries = new List<(string[] Tokens, string Company)>(maxCorpusEntries);
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

                entries.Add((entryTokens, entry.Company));
            }

            // ── Primary path: 5-gram shingle containment (verbatim copies) ──
            var matches = new List<MatchInfo>();

            foreach (var (entryTokens, company) in entries)
            {
                var entryShingles = BuildShingles(entryTokens);
                if (entryShingles.Count == 0)
                    continue;

                var containment = ComputeContainment(postingShingles, entryShingles);
                if (containment >= ContainmentThreshold)
                {
                    matches.Add(new MatchInfo(company, containment));
                }
            }

            if (matches.Count > 0)
            {
                var (score, confidence) = ComputeScoreAndConfidence(matches);
                return Task.FromResult<SignalResult?>(new SignalResult
                {
                    SignalName = Name,
                    Score = score,
                    Confidence = confidence,
                    Evidence = BuildEvidence(matches),
                    Tier = Tier
                });
            }

            // ── Adaptive path: corpus-learned weighted token overlap ────────
            var adaptive = TryWeightedMatches(postingTokens, entries);
            if (adaptive.Count == 0)
                return Task.FromResult<SignalResult?>(null);

            var (wScore, wConfidence) = ComputeWeightedScoreAndConfidence(adaptive);
            return Task.FromResult<SignalResult?>(new SignalResult
            {
                SignalName = Name,
                Score = wScore,
                Confidence = wConfidence,
                Evidence = BuildWeightedEvidence(adaptive),
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

    // ── Adaptive path: corpus-learned weighted token overlap ─────────────

    /// <summary>
    /// IDF-weighted distinct-token overlap against every corpus entry. Tokens
    /// that appear across most of the batch ("candidate", "experience") weigh
    /// almost nothing; tokens rare in the batch are what actually identifies a
    /// shared template. Three conditions must line up for a match:
    /// 1. weighted containment ≥ <see cref="WeightedThreshold"/>,
    /// 2. raw distinct-token overlap ≥ <see cref="MinDistinctOverlap"/>,
    /// 3. <b>rare-token mass</b> — the posting's terms that are rare in this
    ///    batch (df ≤ N/3) must be at least 60% shared by the entry. This is
    ///    the condition that says "the distinctive words got copied", and it
    ///    keeps two postings of pure batch-boilerplate (raw overlap near 1.0
    ///    but no rare token in common) from firing.
    /// Gated on <see cref="AdaptiveMinCorpusEntries"/> entries: smaller batches
    /// carry no usable term statistics.
    /// </summary>
    public static List<WeightedMatchInfo> TryWeightedMatches(
        string[] postingTokens, List<(string[] Tokens, string Company)> entries)
    {
        if (entries.Count < AdaptiveMinCorpusEntries)
            return new List<WeightedMatchInfo>();

        var postingSet = new HashSet<string>(postingTokens, StringComparer.Ordinal);
        if (postingSet.Count == 0)
            return new List<WeightedMatchInfo>();

        // Document frequency over the usable batch.
        var df = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (entryTokens, _) in entries)
            foreach (var token in new HashSet<string>(entryTokens, StringComparer.Ordinal))
                df[token] = df.TryGetValue(token, out var count) ? count + 1 : 1;

        var n = entries.Count;
        var rareFrequencyCeil = Math.Max(1, n / 3); // df at/below this = distinctive in this batch

        double postingMass = 0, postingRareMass = 0;
        foreach (var token in postingSet)
        {
            var weight = IdfWeight(token, df, n);
            postingMass += weight;
            if (IsRare(token, df, rareFrequencyCeil))
                postingRareMass += weight;
        }

        // A posting with no distinctive vocabulary at all cannot support a
        // "shared template" claim — stay silent.
        if (postingMass <= 0 || postingRareMass <= 0)
            return new List<WeightedMatchInfo>();

        var matches = new List<WeightedMatchInfo>();
        foreach (var (entryTokens, company) in entries)
        {
            var entrySet = new HashSet<string>(entryTokens, StringComparer.Ordinal);

            double sharedWeight = 0, sharedRareWeight = 0;
            int sharedCount = 0;

            foreach (var token in entrySet)
            {
                if (!postingSet.Contains(token))
                    continue;

                sharedCount++;
                var weight = IdfWeight(token, df, n);
                sharedWeight += weight;
                if (IsRare(token, df, rareFrequencyCeil))
                    sharedRareWeight += weight;
            }

            var weighted = sharedWeight / postingMass;
            var raw = (double)sharedCount / postingSet.Count;
            var rareShare = sharedRareWeight / postingRareMass;

            if (weighted >= WeightedThreshold &&
                raw >= MinDistinctOverlap &&
                rareShare >= MinRareMassShare)
            {
                matches.Add(new WeightedMatchInfo(company, weighted, raw));
            }
        }

        return matches;
    }

    private static bool IsRare(string token, Dictionary<string, int> df, int rareFrequencyCeil) =>
        df.TryGetValue(token, out var count) && count <= rareFrequencyCeil;

    private static double IdfWeight(string token, Dictionary<string, int> df, int corpusSize)
    {
        var documentFrequency = df.TryGetValue(token, out var count) ? count : 0;
        // log2(1 + N/(1+df)): present in every entry → ≈1 (heavy suppression as
        // df grows); absent from the batch → log2(1+N) (strong signal).
        return Math.Log2(1.0 + corpusSize / (1.0 + documentFrequency));
    }

    private static (double Score, double Confidence) ComputeWeightedScoreAndConfidence(List<WeightedMatchInfo> matches)
    {
        var bestW = matches.Max(m => m.WeightedOverlap);

        // Reordered copies are weaker evidence than verbatim shingle matches,
        // so the ceiling stays at 0.55 (the shingle path starts at 0.40).
        var score = Math.Min(0.55, 0.40 + (bestW - WeightedThreshold) / (1.0 - WeightedThreshold) * 0.15);
        score = Math.Round(score, 2);

        var confidence = matches.Count switch
        {
            1 => 0.40,
            2 => 0.55,
            _ => 0.65
        };

        return (score, confidence);
    }

    private static string[] BuildWeightedEvidence(List<WeightedMatchInfo> matches)
    {
        var best = matches.OrderByDescending(m => m.WeightedOverlap).First();
        var pct = (int)Math.Round(best.WeightedOverlap * 100);

        var lines = new List<string>
        {
            $"{pct}% of this description's distinctive wording (weighted by how rare each word is across today's search batch) also appears in a posting from a different company ('{best.Company}'), even though the sentence order differs." +
            " Reordered or lightly reworded copies point to a shared template."
        };

        if (matches.Count > 1)
        {
            lines.Add($"In total, {matches.Count} postings from unrelated companies share most of this wording.");
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

    public sealed record WeightedMatchInfo(string Company, double WeightedOverlap, double DistinctTokenOverlap);}
