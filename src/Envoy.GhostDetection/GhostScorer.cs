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
        var results = new List<SignalResult>();

        // localOnly skips network-bound signals (e.g. ATS cross-check) so callers scoring
        // many postings at once — like the discovery list — stay fast and don't fan out
        // one request per posting.
        var signals = localOnly ? _signals.Where(s => !s.RequiresNetwork) : _signals;

        foreach (var signal in signals)
        {
            try
            {
                var result = await signal.EvaluateAsync(posting, ct);
                if (result != null)
                    results.Add(result);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Signal {Signal} threw during evaluation; skipping.", signal.Name);
            }
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
            .SelectMany(r => r.Evidence)
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
}
