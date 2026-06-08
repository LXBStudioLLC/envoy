using Envoy.GhostDetection.Models;

namespace Envoy.GhostDetection.Signals;

/// <summary>
/// <strong>TODO:</strong> Implement cross-referencing against DOL OFLC PERM disclosure data.
///
/// <para>Data source: U.S. Department of Labor, Office of Foreign Labor Certification (OFLC)
/// PERM disclosure data — publicly available quarterly Excel/CSV files at
/// https://www.dol.gov/agencies/eta/foreign-labor/performance.</para>
///
/// <para>Expected columns: employer name, job title, work location, prevailing wage,
/// SOC code, case status, filing date.</para>
///
/// <para><strong>IMPORTANT WARNING:</strong> A match between a job posting and a PERM filing
/// means the employer sponsored someone for a <em>similar</em> role, NOT that this exact
/// posting is the PERM labor-market test ad. Large employers file many PERMs and also
/// hire real people for the same titles. This signal must never auto-condemn a posting.
/// If a match is found, present it as evidence ("Employer filed PERM for similar title
/// in same location") with a <strong>low Score</strong> and moderate Confidence.</para>
///
/// <para>Acceptance criteria: implements <see cref="IGhostSignal"/>, returns null when
/// PERM data is unavailable or no match, has fixture tests, no network calls in tests.</para>
/// </summary>
public class PermFilingSignal : IGhostSignal
{
    public string Name => "PERM Filing Cross-Check";
    public SignalTier Tier => SignalTier.Probabilistic;

    public Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default)
    {
        // TODO: download and cache DOL PERM CSV, fuzzy-match employer+title+location,
        // return SignalResult with low score and explicit non-condemnation evidence.
        return Task.FromResult<SignalResult?>(null);
    }
}
