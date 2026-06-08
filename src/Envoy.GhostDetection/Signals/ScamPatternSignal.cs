using Envoy.GhostDetection.Models;

namespace Envoy.GhostDetection.Signals;

/// <summary>
/// <strong>TODO:</strong> Detect classic scam patterns in job postings.
///
/// <para>Patterns to flag (all Deterministic — hard rules, no ML needed):
/// <list type="bullet">
/// <item>Off-platform redirects: Telegram, WhatsApp, Signal contact instructions</item>
/// <item>Upfront PII / bank asks: "send SSN to apply", "pay for background check"</item>
/// <item>Lookalike domains: company name slightly misspelled in email/URL</item>
/// <item>Unrealistic compensation: 10× market rate for the title + location</item>
/// <item>Stock-photo logos or no verifiable company registration</item>
/// </list>
/// </para>
///
/// <para>Data source: <see cref="JobPosting.DescriptionText"/>,
/// <see cref="JobPosting.Url"/>, and <see cref="JobPosting.CompanyName"/>.
/// No external network calls — all regex / string analysis.</para>
///
/// <para>Acceptance criteria: implements <see cref="IGhostSignal"/>, returns null when
/// no scam patterns detected, has fixture tests, no network calls in tests.</para>
/// </summary>
public class ScamPatternSignal : IGhostSignal
{
    public string Name => "Scam Pattern";
    public SignalTier Tier => SignalTier.Deterministic;

    public Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default)
    {
        // TODO: regex-scan DescriptionText and Url for scam indicators,
        // return high-score SignalResult when hard patterns are matched.
        return Task.FromResult<SignalResult?>(null);
    }
}
