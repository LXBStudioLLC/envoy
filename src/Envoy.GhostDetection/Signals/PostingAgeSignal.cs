using Envoy.GhostDetection.Models;

namespace Envoy.GhostDetection.Signals;

/// <summary>
/// Probabilistic signal: score a posting's age relative to role-type baselines.
/// A 90-day senior engineering search is normal; a 90-day entry-level posting is
/// suspicious. We normalize by seniority inferred from the job title.
/// </summary>
public class PostingAgeSignal : IGhostSignal
{
    public string Name => "Posting Age";
    public SignalTier Tier => SignalTier.Probabilistic;
    public bool RequiresNetwork => false;

    public Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default)
    {
        var ageDays = ComputeAgeDays(posting);
        if (ageDays == null)
            return Task.FromResult<SignalResult?>(null);

        var seniority = InferSeniority(posting.JobTitle);
        var baselineDays = BaselineForSeniority(seniority);

        // Fresh enough — no opinion
        if (ageDays.Value <= baselineDays)
            return Task.FromResult<SignalResult?>(null);

        var (score, confidence) = ComputeScoreAndConfidence(ageDays.Value, baselineDays, seniority);
        var evidence = BuildEvidence(ageDays.Value, baselineDays, seniority, posting.JobTitle);

        return Task.FromResult<SignalResult?>(new SignalResult
        {
            SignalName = Name,
            Score = score,
            Confidence = confidence,
            Evidence = evidence,
            Tier = Tier
        });
    }

    // ── Age computation ──────────────────────────────────────────────────

    private static int? ComputeAgeDays(JobPosting posting)
    {
        var referenceDate = posting.PostedAtUtc ?? posting.LastUpdatedUtc;
        if (referenceDate == null)
            return null;

        var age = DateTime.UtcNow - referenceDate.Value;
        return (int)age.TotalDays;
    }

    // ── Seniority inference ──────────────────────────────────────────────

    private static Seniority InferSeniority(string title)
    {
        var t = (title ?? string.Empty).ToLowerInvariant();

        // Executive / very senior
        if (ContainsAny(t, "vice president", "head of") ||
            ContainsAnyWord(t, "chief", "cto", "cfo", "cio", "coo", "vp", "director"))
            return Seniority.Executive;

        // Senior
        if (ContainsAny(t, "sr ", "sr.") ||
            ContainsAnyWord(t, "senior", "staff", "principal", "lead", "architect", "manager", "mgr"))
            return Seniority.Senior;

        // Junior / entry
        if (ContainsAny(t, "jr ", "jr.", "co-op") ||
            ContainsAnyWord(t, "junior", "entry", "intern", "associate", "trainee", "apprentice", "coop", "coordinator"))
            return Seniority.Junior;

        return Seniority.Mid;
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (haystack.Contains(n, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Like <see cref="ContainsAny"/> but requires the keyword to appear as a whole word
    /// (or at a word boundary) so "coordinator" does not match "director".
    /// </summary>
    private static bool ContainsAnyWord(string haystack, params string[] needles)
    {
        foreach (var n in needles)
        {
            int idx = haystack.IndexOf(n, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;

            bool startBoundary = idx == 0 || !char.IsLetterOrDigit(haystack[idx - 1]);
            bool endBoundary = idx + n.Length == haystack.Length || !char.IsLetterOrDigit(haystack[idx + n.Length]);

            if (startBoundary && endBoundary)
                return true;
        }
        return false;
    }

    // ── Baselines ──────────────────────────────────────────────────────

    private static int BaselineForSeniority(Seniority s) => s switch
    {
        Seniority.Junior => 30,
        Seniority.Mid => 60,
        Seniority.Senior => 90,
        Seniority.Executive => 120,
        _ => 60
    };

    private static string SeniorityLabel(Seniority s) => s switch
    {
        Seniority.Junior => "entry-level / junior",
        Seniority.Mid => "mid-level",
        Seniority.Senior => "senior / lead",
        Seniority.Executive => "executive / director",
        _ => "unspecified-level"
    };

    // ── Scoring ────────────────────────────────────────────────────────

    private static (double Score, double Confidence) ComputeScoreAndConfidence(int ageDays, int baseline, Seniority seniority)
    {
        double score;
        if (ageDays <= baseline * 2)
        {
            // baseline -> 2x baseline maps to 0.30 -> 0.55
            var t = (double)(ageDays - baseline) / baseline;
            score = 0.30 + t * 0.25;
        }
        else if (ageDays <= baseline * 3)
        {
            // 2x -> 3x baseline maps to 0.60 -> 0.80
            var t = (double)(ageDays - baseline * 2) / baseline;
            score = 0.60 + t * 0.20;
        }
        else
        {
            // Beyond 3x baseline — strong signal but cap below deterministic territory
            score = Math.Min(0.88, 0.80 + (ageDays - baseline * 3) / 500.0);
        }

        // Confidence: higher when seniority is clearly inferred and we have a real PostedAtUtc
        double confidence = 0.65;
        if (seniority != Seniority.Mid)
            confidence += 0.10; // title had clear seniority keyword
        // (we can't tell from JobPosting whether PostedAtUtc or LastUpdatedUtc was used,
        // so we keep the base confidence moderate)

        confidence = Math.Min(0.92, confidence);
        return (Math.Round(score, 2), Math.Round(confidence, 2));
    }

    // ── Evidence ───────────────────────────────────────────────────────

    private static string[] BuildEvidence(int ageDays, int baseline, Seniority seniority, string title)
    {
        var lines = new List<string>
        {
            $"Posted {ageDays} days ago for a {SeniorityLabel(seniority)} role (typical fill time: ~{baseline} days)."
        };

        if (ageDays > baseline * 3)
            lines.Add("This is well beyond the typical fill window for this seniority level.");
        else if (ageDays > baseline * 2)
            lines.Add("This is significantly older than the typical fill window for this seniority level.");
        else
            lines.Add("This is somewhat older than the typical fill window for this seniority level.");

        return lines.ToArray();
    }

    // ── Internal model ─────────────────────────────────────────────────

    private enum Seniority { Junior, Mid, Senior, Executive }
}
