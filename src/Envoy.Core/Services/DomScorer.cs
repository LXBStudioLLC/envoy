using System.Text.Json.Serialization;

namespace Envoy.Core.Services;

public class DomCandidate
{
    public string Tag { get; set; } = string.Empty;
    public Dictionary<string, string> Attributes { get; set; } = new();
    public string? TextContent { get; set; }
    [JsonPropertyName("labelText")]
    public string? LabelText { get; set; }
    [JsonPropertyName("ancestors")]
    public List<string> AncestorChain { get; set; } = new();
    [JsonPropertyName("siblingsBefore")]
    public List<string> SiblingsBefore { get; set; } = new();
    [JsonPropertyName("siblingsAfter")]
    public List<string> SiblingsAfter { get; set; } = new();
    [JsonPropertyName("positionIndex")]
    public int PositionIndex { get; set; }
    [JsonPropertyName("cssSelector")]
    public string CssSelector { get; set; } = string.Empty;
}

public sealed class CandidateScoreResult(string cssSelector, double score, bool aboveThreshold)
{
    public string CssSelector { get; } = cssSelector;
    public double Score { get; } = score;
    public bool AboveThreshold { get; } = aboveThreshold;
}

public static class DomScorer
{
    private static readonly Dictionary<string, double> AttributeWeights = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = 3.0,
        ["type"] = 3.0,
        ["id"] = 2.5,
        ["placeholder"] = 2.0,
        ["aria-label"] = 2.0,
        ["data-automation-id"] = 2.0,
        ["role"] = 1.5,
        ["title"] = 1.5,
        ["for"] = 1.5,
        ["autocomplete"] = 1.0,
        ["required"] = 0.5,
        ["class"] = 0.5,
    };

    public static double WeightAttributes = 0.30;
    public static double WeightLabel = 0.30;
    public static double WeightAncestor = 0.15;
    public static double WeightSibling = 0.15;
    public static double WeightPosition = 0.10;

    public static CandidateScoreResult? FindBestMatch(
        Fingerprint fingerprint,
        List<DomCandidate> candidates,
        double threshold)
    {
        IEnumerable<DomCandidate> filtered = candidates;
        if (!string.IsNullOrEmpty(fingerprint.Tag))
            filtered = candidates.Where(c =>
                c.Tag.Equals(fingerprint.Tag, StringComparison.OrdinalIgnoreCase));

        var matchable = filtered.ToList();
        if (matchable.Count == 0) return null;

        DomCandidate? best = null;
        double bestScore = double.MinValue;

        foreach (var candidate in matchable)
        {
            var score = ScoreCandidate(fingerprint, candidate);
            if (score >= bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        if (best == null) return null;
        return new CandidateScoreResult(best.CssSelector, bestScore, bestScore >= threshold);
    }

    public static double ScoreCandidate(Fingerprint fp, DomCandidate candidate)
    {
        double attrScore = ScoreAttributes(fp, candidate);
        double labelScore = ScoreLabel(fp, candidate);
        double ancestorScore = ScoreAncestors(fp, candidate);
        double siblingScore = ScoreSiblings(fp, candidate);
        double posScore = ScorePosition(fp, candidate);

        return WeightAttributes * attrScore
             + WeightLabel * labelScore
             + WeightAncestor * ancestorScore
             + WeightSibling * siblingScore
             + WeightPosition * posScore;
    }

    public static double ScoreAttributes(Fingerprint fp, DomCandidate candidate)
    {
        if (fp.Attributes == null || fp.Attributes.Count == 0) return 0;
        if (candidate.Attributes.Count == 0) return 0;

        double totalWeight = 0;
        double matchedWeight = 0;

        foreach (var (key, fpValue) in fp.Attributes)
        {
            if (string.IsNullOrEmpty(fpValue)) continue;
            double attrWeight = AttributeWeights.TryGetValue(key, out var w) ? w : 1.0;
            totalWeight += attrWeight;

            if (candidate.Attributes.TryGetValue(key, out var candValue))
            {
                if (AttributeValuesMatch(fpValue, candValue))
                    matchedWeight += attrWeight;
            }
        }

        return totalWeight == 0 ? 0 : matchedWeight / totalWeight;
    }

    public static double ScoreLabel(Fingerprint fp, DomCandidate candidate)
    {
        if (string.IsNullOrEmpty(fp.LabelText)) return 0;

        var candidateLabel = candidate.LabelText ?? "";
        if (string.IsNullOrEmpty(candidateLabel)) return 0;

        return NormalizedSimilarity(fp.LabelText, candidateLabel);
    }

    public static double ScoreAncestors(Fingerprint fp, DomCandidate candidate)
    {
        if (fp.AncestorChain == null || fp.AncestorChain.Count == 0) return 0;
        if (candidate.AncestorChain.Count == 0) return 0;

        int matchLen = LongestCommonSuffixLength(fp.AncestorChain, candidate.AncestorChain);
        return (double)matchLen / fp.AncestorChain.Count;
    }

    public static double ScoreSiblings(Fingerprint fp, DomCandidate candidate)
    {
        double beforeScore = ScoreSiblingOverlap(fp.SiblingsBefore, candidate.SiblingsBefore);
        double afterScore = ScoreSiblingOverlap(fp.SiblingsAfter, candidate.SiblingsAfter);

        bool hasBefore = fp.SiblingsBefore != null && fp.SiblingsBefore.Count > 0;
        bool hasAfter = fp.SiblingsAfter != null && fp.SiblingsAfter.Count > 0;

        int total = (hasBefore ? 1 : 0) + (hasAfter ? 1 : 0);
        if (total == 0) return 0;

        return (beforeScore + afterScore) / total;
    }

    public static double ScorePosition(Fingerprint fp, DomCandidate candidate)
    {
        if (fp.PositionIndex == null) return 0;
        int diff = Math.Abs(fp.PositionIndex.Value - candidate.PositionIndex);
        return Math.Max(0, 1.0 - diff / 10.0);
    }

    private static bool AttributeValuesMatch(string fpValue, string candValue)
    {
        if (string.Equals(fpValue, candValue, StringComparison.OrdinalIgnoreCase))
            return true;

        var fpTokens = Tokenize(fpValue);
        var candTokens = Tokenize(candValue);

        if (fpTokens.Count == 0 || candTokens.Count == 0) return false;

        int overlap = fpTokens.Intersect(candTokens, StringComparer.OrdinalIgnoreCase).Count();
        return overlap > 0 && (double)overlap / fpTokens.Count >= 0.5;
    }

    private static HashSet<string> Tokenize(string value)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in value.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries))
            tokens.Add(part);
        return tokens;
    }

    public static double NormalizedSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
        int distance = LevenshteinDistance(a, b);
        int maxLen = Math.Max(a.Length, b.Length);
        return maxLen == 0 ? 1.0 : 1.0 - (double)distance / maxLen;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b.Length;
        if (string.IsNullOrEmpty(b)) return a.Length;

        var dp = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) dp[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + cost);
            }
        }

        return dp[a.Length, b.Length];
    }

    private static int LongestCommonSuffixLength(List<string> a, List<string> b)
    {
        int ia = a.Count - 1;
        int ib = b.Count - 1;
        int count = 0;

        while (ia >= 0 && ib >= 0)
        {
            if (SelectorSegmentMatches(a[ia], b[ib]))
            {
                count++;
                ia--;
                ib--;
            }
            else
            {
                break;
            }
        }

        return count;
    }

    private static bool SelectorSegmentMatches(string fpSeg, string candSeg)
    {
        if (string.Equals(fpSeg, candSeg, StringComparison.OrdinalIgnoreCase))
            return true;

        string fpBase = fpSeg.Split('.')[0].Split('#')[0];
        string candBase = candSeg.Split('.')[0].Split('#')[0];

        if (string.Equals(fpBase, candBase, StringComparison.OrdinalIgnoreCase))
            return true;

        return NormalizedSimilarity(fpSeg, candSeg) > 0.7;
    }

    private static double ScoreSiblingOverlap(List<string>? fpSiblings, List<string> candSiblings)
    {
        if (fpSiblings == null || fpSiblings.Count == 0) return 0;
        if (candSiblings.Count == 0) return 0;

        int matched = 0;
        foreach (var fpText in fpSiblings)
        {
            if (string.IsNullOrEmpty(fpText)) continue;
            foreach (var candText in candSiblings)
            {
                if (string.IsNullOrEmpty(candText)) continue;
                if (NormalizedSimilarity(fpText, candText) > 0.6)
                {
                    matched++;
                    break;
                }
            }
        }

        return (double)matched / fpSiblings.Count;
    }
}