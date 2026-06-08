using Envoy.GhostDetection.Models;

namespace Envoy.GhostDetection.Signals;

/// <summary>
/// <strong>TODO:</strong> Detect freshness-gaming via ATS created-vs-updated timestamps.
///
/// <para>If a posting's LastUpdatedUtc is much more recent than PostedAtUtc but
/// the description hasn't materially changed, the employer may be reposting the same
/// role to game "new" sorting on job boards. Compare hash or similarity of
/// description text between updates.</para>
///
/// <para>Data source: <see cref="JobPosting.PostedAtUtc"/> and
/// <see cref="JobPosting.LastUpdatedUtc"/>. No external network calls.</para>
///
/// <para>Acceptance criteria: implements <see cref="IGhostSignal"/>, returns null when
/// timestamps are missing or equal, has fixture tests, no network calls in tests.</para>
/// </summary>
public class RepostFrequencySignal : IGhostSignal
{
    public string Name => "Repost Frequency";
    public SignalTier Tier => SignalTier.Weak;

    public Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default)
    {
        // TODO: compare PostedAtUtc vs LastUpdatedUtc delta, check for repeated
        // reposting without material description change.
        return Task.FromResult<SignalResult?>(null);
    }
}
