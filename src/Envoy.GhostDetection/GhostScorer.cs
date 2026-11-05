using Envoy.GhostDetection.Models;
using Microsoft.Extensions.Logging;

namespace Envoy.GhostDetection;

/// <summary>
/// Aggregates all registered <see cref="IGhostSignal"/> implementations into a
/// single <see cref="GhostScore"/> for a given job posting.
/// </summary>
public class GhostScorer
{
    private readonly IEnumerable<IGhostSignal> _signals;
    private readonly ILogger<GhostScorer> _log;

    // ── Threshold constants ────────────────────────────────────────────
    private const double DeterministicScoreThreshold = 0.80;
    private const double DeterministicConfidenceThreshold = 0.70;
    private const int ProbabilisticElevatedCount = 2;
    private const double ProbabilisticScoreThreshold = 0.60;
    private const double ProbabilisticWeight = 1.0;
    private const double WeakWeight = 0.05;

    public GhostScorer(IEnumerable<IGhostSignal> signals, ILogger<GhostScorer> log)
    {
        _signals = signals;
        _log = log;
    }

    public async Task<GhostScore> ScoreAsync(JobPosting posting, CancellationToken ct = default, bool localOnly = false)
    {
        // APP-08: signals run concurrently. Scoring happens once per posting
        // (twice through the discovery→apply round trip today), and the
        // network signals dominate wall time; a sequential foreach turned
        // nine independent checks into their SUM. Failures stay per-signal:
        // a throwing signal is skipped with a warning, exactly as before.
        var signals = localOnly
            ? _signals.Where(s => !s.RequiresNetwork).ToArray()
            : _signals.ToArray();

        List<SignalResult> results;
        if (signals.Length <= 1)
        {
            results = await RunSignalsSequentialWait(signals, posting, ct);
        }
        else
        {
            var evaluations = signals.Select(s => EvaluateSafeAsync(s, posting, ct)).ToArray();
            var settled = await Task.WhenAll(evaluations);
            results = settled.Where(r => r != null).Cast<SignalResult>().ToList();
        }

        var deterministic = results.Where(r => r.Tier == SignalTier.Deterministic).ToList();
        var probabilistic = results.Where(r => r.Tier == SignalTier.Probabilistic).ToList();
        var weak = results.Where(r => r.Tier == SignalTier.Weak).ToList();

        // ── Band decision ────────────────────────────────────────────────
        RiskBand band;
        double rawRisk;

        if (deterministic.Any(d => d.Score >= DeterministicScoreThreshold && d.Confidence >= DeterministicConfidenceThreshold))
        {
            band = RiskBand.High;
            rawRisk = Math.Min(100, deterministic
                .Where(d => d.Score >= DeterministicScoreThreshold && d.Confidence >= DeterministicConfidenceThreshold)
                .Max(d => d.Score * d.Confidence * 100));
        }
        else if (probabilistic.Count(r => r.Score >= ProbabilisticScoreThreshold) >= ProbabilisticElevatedCount)
        {
            band = RiskBand.Elevated;
            rawRisk = Math.Min(100, probabilistic
                .Where(r => r.Score >= ProbabilisticScoreThreshold)
                .Sum(r => r.Score * r.Confidence * ProbabilisticWeight * 50));
        }
        else
        {
            band = RiskBand.Neutral;
            rawRisk = probabilistic.Sum(r => r.Score * r.Confidence * ProbabilisticWeight * 30)
                    + weak.Sum(r => r.Score * r.Confidence * WeakWeight * 10);
            rawRisk = Math.Min(100, rawRisk);
        }

        // ── TopEvidence: strongest lines, capped ─────────────────────────
        var topEvidence = results
            .OrderByDescending(r => r.Confidence * (r.Tier == SignalTier.Deterministic ? 10 : r.Tier == SignalTier.Probabilistic ? 3 : 1))
            .SelectMany(r => r.Evidence ?? Array.Empty<string>())
            .Distinct()
            .Take(6)
            .ToArray();

        return new GhostScore
        {
            RiskScore = Math.Round(rawRisk, 1),
            Band = band,
            Signals = results.ToArray(),
            TopEvidence = topEvidence
        };
    }

    private async Task<List<SignalResult>> RunSignalsSequentialWait(IEnumerable<IGhostSignal> signals, JobPosting posting, CancellationToken ct)
    {
        var results = new List<SignalResult>();
        foreach (var signal in signals)
        {
            var result = await EvaluateSafeAsync(signal, posting, ct);
            if (result != null)
                results.Add(result);
        }
        return results;
    }

    private async Task<SignalResult?> EvaluateSafeAsync(IGhostSignal signal, JobPosting posting, CancellationToken ct)
    {
        // Per-signal capture so one throwing signal can never starve the
        // batch: a bare Task.WhenAll would aggregate the first exception and
        // make the scorer drop every healthy result with it.
        try
        {
            return await signal.EvaluateAsync(posting, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Signal {Signal} threw during evaluation; skipping.", signal.Name);
            return null;
        }
    }
}
