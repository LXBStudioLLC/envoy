using Envoy.GhostDetection.Models;

namespace Envoy.GhostDetection.Signals;

/// <summary>
/// <strong>TODO:</strong> Correlate active layoffs / hiring freezes with continued posting.
///
/// <para>Data sources (all public, no auth required):
/// <list type="bullet">
/// <item>Layoffs.fyi public dataset / API (aggregated from public LinkedIn posts, news)</item>
/// <item>Company SEC 8-K filings (material events, including workforce reductions)</item>
/// <item>Public news article sentiment via RSS/NewsAPI (free tier, no auth for basic queries)</item>
/// </list>
/// </para>
///
/// <para>If a company has disclosed a material layoff or hiring freeze within the
/// last 90 days but is still actively posting the same role, that's probabilistic
/// evidence the posting may be a ghost (HR pipeline hasn't been updated).</para>
///
/// <para>Acceptance criteria: implements <see cref="IGhostSignal"/>, returns null when
/// no freeze/layoff data is found, has fixture tests, no network calls in tests.</para>
/// </summary>
public class HiringFreezeSignal : IGhostSignal
{
    public string Name => "Hiring Freeze";
    public SignalTier Tier => SignalTier.Probabilistic;

    public Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default)
    {
        // TODO: query local cache of layoffs/freeze data by company name,
        // return SignalResult when recent freeze overlaps with active posting.
        return Task.FromResult<SignalResult?>(null);
    }
}
