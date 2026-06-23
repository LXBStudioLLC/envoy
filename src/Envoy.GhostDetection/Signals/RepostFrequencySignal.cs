using Envoy.Core.Services;
using Envoy.GhostDetection.Models;
using System.Text.Json;

namespace Envoy.GhostDetection.Signals;

/// <summary>
/// Weak signal: detect unchanged re-listing of the same job posting over time.
///
    /// <para>Algorithm:
    /// 1. Try PATH A (history-backed): parse caller-supplied snapshots from
    ///    <c>Extra["repost.history"]</c>. Each snapshot has a <c>seenAtUtc</c> and the
    ///    <c>description</c> text at that time.
    /// 2. Snapshots are sorted ascending by <c>seenAtUtc</c>; at most 24 are processed.
    /// 3. A snapshot is usable when <c>seenAtUtc</c> parses and raw description length
    ///    is &gt;= 200 chars. The current posting description must also be &gt;= 200 chars.
    /// 4. Build a timeline: usable snapshots + current posting appended last.
    ///    The current posting always participates in text-pair comparison, but its
    ///    timestamp contributes to span only when <see cref="JobPosting.LastUpdatedUtc"/>
    ///    is present. Span = max(spanTimes) − min(spanTimes) where spanTimes are all
    ///    usable snapshot <c>seenAtUtc</c> values plus <see cref="JobPosting.LastUpdatedUtc"/>
    ///    when present.
    /// 5. For each consecutive pair, compute
    ///    <see cref="DomScorer.NormalizedSimilarity"/> on normalized text.
    ///    A pair with similarity &gt;= 0.95 is an UNCHANGED BUMP.
    /// 6. If unchanged bumps &gt;= 2, score by bump count (2→0.45, 3→0.55, 4+→0.65)
    ///    and add +0.10 if the span is &lt;= 30 days (cap 0.75).
    /// 7. If PATH A has &lt; 2 usable snapshots, fall back to PATH B (timestamps only):
    ///    <see cref="JobPosting.LastUpdatedUtc"/> &gt; <see cref="JobPosting.PostedAtUtc"/>,
    ///    gap &gt;= 45 days → weak signal with score 0.35, confidence 0.40.
    /// 8. Return <c>null</c> when data is missing, malformed, or every update was a
    ///    real edit (no unchanged bumps).
    /// </para>
///
/// <para>Precision rules: default to null when history JSON is malformed or when
/// successive descriptions differ materially. Evidence is hedged, never a verdict.
/// </para>
/// </summary>
public class RepostFrequencySignal : IGhostSignal
{
    public string Name => "Repost Frequency";
    public SignalTier Tier => SignalTier.Weak;
    public bool RequiresNetwork => false;

    private const double UnchangedSimilarityThreshold = 0.95;
    private const int MaxHistoryEntries = 24;
    private const int MinDescriptionLength = 200;
    private const int TimestampGapDays = 45;
    private const int RapidCycleDays = 30;

    private static readonly JsonSerializerOptions HistoryJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default)
    {
        try
        {
            var pathA = TryPathA(posting);
            var pathB = TryPathB(posting);
            return Task.FromResult<SignalResult?>(pathA ?? pathB);
        }
        catch
        {
            return Task.FromResult<SignalResult?>(null);
        }
    }

    // ── PATH A ─────────────────────────────────────────────────────────

    private SignalResult? TryPathA(JobPosting posting)
    {
        if (posting.DescriptionText.Length < MinDescriptionLength)
            return null;

        var historyJson = posting.Extra.TryGetValue("repost.history", out var historyValue)
            ? historyValue
            : null;

        if (string.IsNullOrWhiteSpace(historyJson))
            return null;

        List<HistorySnapshot> snapshots;
        try
        {
            snapshots = JsonSerializer.Deserialize<List<HistorySnapshot>>(historyJson, HistoryJsonOptions)
                ?? new List<HistorySnapshot>();
        }
        catch (JsonException)
        {
            return null;
        }

        // Sort, filter, cap
        var usable = snapshots
            .Where(s => s.SeenAtUtc != default && !string.IsNullOrWhiteSpace(s.Description) && s.Description.Length >= MinDescriptionLength)
            .OrderBy(s => s.SeenAtUtc)
            .Take(MaxHistoryEntries)
            .ToList();

        if (usable.Count < 2)
            return null;

        // Build timeline for text comparison: snapshots + current posting appended last
        var timeline = new List<TimelineEntry>(usable.Count + 1);
        foreach (var u in usable)
            timeline.Add(new TimelineEntry(u.SeenAtUtc, u.Description));
        timeline.Add(new TimelineEntry(posting.LastUpdatedUtc ?? DateTime.MinValue, posting.DescriptionText));

        // Span times: snapshot SeenAtUtc values + LastUpdatedUtc when present (current posting contributes NO timestamp when null)
        var spanTimes = usable.Select(u => u.SeenAtUtc).ToList();
        if (posting.LastUpdatedUtc.HasValue)
            spanTimes.Add(posting.LastUpdatedUtc.Value);

        // Count unchanged bumps
        int unchangedBumps = 0;
        for (int i = 0; i < timeline.Count - 1; i++)
        {
            var prev = NormalizeText(timeline[i].Description);
            var next = NormalizeText(timeline[i + 1].Description);
            var sim = DomScorer.NormalizedSimilarity(prev, next);
            if (sim >= UnchangedSimilarityThreshold)
                unchangedBumps++;
        }

        if (unchangedBumps < 2)
            return null;

        var (score, confidence) = ComputeScoreAndConfidencePathA(unchangedBumps, spanTimes);
        var evidence = BuildEvidencePathA(unchangedBumps, spanTimes);

        return new SignalResult
        {
            SignalName = Name,
            Score = score,
            Confidence = confidence,
            Evidence = evidence,
            Tier = Tier
        };
    }

    private static (double Score, double Confidence) ComputeScoreAndConfidencePathA(int unchangedBumps, List<DateTime> spanTimes)
    {
        double score = unchangedBumps switch
        {
            2 => 0.45,
            3 => 0.55,
            _ => 0.65
        };

        if (spanTimes.Count >= 2)
        {
            var span = spanTimes.Max() - spanTimes.Min();
            if (span.TotalDays <= RapidCycleDays)
                score += 0.10;
        }

        score = Math.Min(0.75, score);
        score = Math.Round(score, 2);

        double confidence = unchangedBumps >= 3 ? 0.70 : 0.60;
        confidence = Math.Round(confidence, 2);

        return (score, confidence);
    }

    private static string[] BuildEvidencePathA(int unchangedBumps, List<DateTime> spanTimes)
    {
        var days = 0;
        if (spanTimes.Count >= 2)
        {
            var span = spanTimes.Max() - spanTimes.Min();
            days = (int)span.TotalDays;
        }

        var lines = new List<string>
        {
            $"This posting's text has appeared {unchangedBumps + 1} times across {days} days without meaningful changes, a pattern consistent with re-listing a job to make it look new."
        };

        if (spanTimes.Count >= 2)
        {
            var span = spanTimes.Max() - spanTimes.Min();
            if (span.TotalDays <= RapidCycleDays)
            {
                lines.Add("The rapid re-listing cycle suggests this posting may be kept artificially fresh.");
            }
        }

        return lines.Take(3).ToArray();
    }

    // ── PATH B ─────────────────────────────────────────────────────────

    private SignalResult? TryPathB(JobPosting posting)
    {
        if (posting.PostedAtUtc == null || posting.LastUpdatedUtc == null)
            return null;

        if (posting.LastUpdatedUtc <= posting.PostedAtUtc)
            return null;

        var gap = posting.LastUpdatedUtc.Value - posting.PostedAtUtc.Value;
        if (gap.TotalDays < TimestampGapDays)
            return null;

        var days = (int)gap.TotalDays;
        var evidence = new[]
        {
            $"This listing was updated {days} days after it was first posted; with no edit history available, a long-delayed update can indicate re-listing to refresh its apparent age — or simply a legitimate revision."
        };

        return new SignalResult
        {
            SignalName = Name,
            Score = 0.35,
            Confidence = 0.40,
            Evidence = evidence,
            Tier = Tier
        };
    }

    // ── Text normalization ─────────────────────────────────────────────

    private static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
            else
                sb.Append(' ');
        }

        var tokens = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", tokens);
    }

    // ── Internal models ──────────────────────────────────────────────────

    private sealed class HistorySnapshot
    {
        public DateTime SeenAtUtc { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    private sealed record TimelineEntry(DateTime Time, string Description);
}
