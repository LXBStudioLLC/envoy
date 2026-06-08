using Envoy.GhostDetection.Models;

namespace Envoy.GhostDetection.Signals;

/// <summary>
/// <strong>TODO:</strong> Score a posting's age relative to role-type baselines.
///
/// <para>A 90-day senior engineering search is normal; a 90-day entry-level barista
/// posting is suspicious. Normalize by seniority level (extracted from title keywords
/// like "Senior", "Staff", "Principal", "Intern", "Entry") and by field (tech vs.
/// non-tech have different velocity baselines).</para>
///
/// <para>Data source: <see cref="JobPosting.PostedAtUtc"/> and
/// <see cref="JobPosting.LastUpdatedUtc"/>. No external network calls.</para>
///
/// <para>Acceptance criteria: implements <see cref="IGhostSignal"/>, returns null when
/// dates are missing, has fixture tests, no network calls in tests.</para>
/// </summary>
public class PostingAgeSignal : IGhostSignal
{
    public string Name => "Posting Age";
    public SignalTier Tier => SignalTier.Probabilistic;

    public Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default)
    {
        // TODO: compute age delta, normalize against role-type baseline,
        // return SignalResult with elevated score for stale postings in fast-turnover roles.
        return Task.FromResult<SignalResult?>(null);
    }
}
