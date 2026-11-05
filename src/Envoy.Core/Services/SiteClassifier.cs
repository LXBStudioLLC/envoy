namespace Envoy.Core.Services;

/// <summary>
/// Classifies posting URLs into channel types (APP-11) so the automation
/// boundary in AGENTS.md is enforced in code, not prose: aggregators are
/// copilot-only — no automatic submission may ever target LinkedIn, Indeed,
/// ZipRecruiter, and friends, now or in any future mode.
/// </summary>
public static class SiteClassifier
{
    public enum SiteChannel
    {
        /// <summary>The company's own ATS board (Greenhouse, Lever, Ashby…). Eligible for every mode.</summary>
        CompanyAts,

        /// <summary>A job-listing intermediary. Never auto-submits; fill stays human-gated forever.</summary>
        Aggregator,

        /// <summary>Unrecognized domain: treated as cautious — NOT eligible for automatic submission.</summary>
        Unknown
    }

    /// <summary>
    /// Aggregator domains. Company/ATS boards are everything NOT here — the
    /// allow-unknown-by-default posture is deliberately NOT taken; anything
    /// not recognized as an aggregator still cannot be auto-submitted unless
    /// it matches the known-ATS list, keeping the blast radius honest.
    /// </summary>
    private static readonly Dictionary<string, SiteChannel> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Aggregators / job boards (copilot-only forever) ─────────────
        ["linkedin.com"] = SiteChannel.Aggregator,
        ["indeed.com"] = SiteChannel.Aggregator,
        ["ziprecruiter.com"] = SiteChannel.Aggregator,
        ["glassdoor.com"] = SiteChannel.Aggregator,
        ["monster.com"] = SiteChannel.Aggregator,
        ["dice.com"] = SiteChannel.Aggregator,
        ["simplyhired.com"] = SiteChannel.Aggregator,
        ["stepstone.de"] = SiteChannel.Aggregator,
        ["reed.co.uk"] = SiteChannel.Aggregator,
        ["totaljobs.com"] = SiteChannel.Aggregator,
        ["careerbuilder.com"] = SiteChannel.Aggregator,
        ["muse.com"] = SiteChannel.Aggregator,
        ["instahyre.com"] = SiteChannel.Aggregator,
        ["naukri.com"] = SiteChannel.Aggregator,
        ["glassdoor.de"] = SiteChannel.Aggregator,
        ["angel.com"] = SiteChannel.Aggregator,          // wellfound/angellist job pages
        ["wellfound.com"] = SiteChannel.Aggregator,

        // ── Company-side ATS boards (eligible for every mode) ──────────
        ["boards.greenhouse.io"] = SiteChannel.CompanyAts,
        ["job-boards.greenhouse.io"] = SiteChannel.CompanyAts,
        ["myworkdayjobs.com"] = SiteChannel.CompanyAts,
        ["myworkdaysite.com"] = SiteChannel.CompanyAts,
        ["smartrecruiters.com"] = SiteChannel.CompanyAts,
        ["jobs.lever.co"] = SiteChannel.CompanyAts,
        ["jobs.astill.net"] = SiteChannel.CompanyAts,
        ["jobs.ashbyhq.com"] = SiteChannel.CompanyAts,
        ["apply.workable.com"] = SiteChannel.CompanyAts,
        ["www.recruitee.com"] = SiteChannel.CompanyAts,
        ["recruitee.com"] = SiteChannel.CompanyAts,
        ["icims.com"] = SiteChannel.CompanyAts,
        ["taleo.net"] = SiteChannel.CompanyAts,
        ["successfactors.com"] = SiteChannel.CompanyAts,
        ["8fold.co"] = SiteChannel.CompanyAts,
        ["teamtailor.com"] = SiteChannel.CompanyAts,
        ["personio.de"] = SiteChannel.CompanyAts,
        ["gem.com"] = SiteChannel.CompanyAts,
        ["pinpointhq.com"] = SiteChannel.CompanyAts,
        ["hi4careers.com"] = SiteChannel.CompanyAts,
        ["applytojob.com"] = SiteChannel.CompanyAts
    };

    /// <summary>
    /// Classifies a URL. Subdomains of a known host inherit the host's class
    /// (e.g. a tenant board like <c>acme.wd5.myworkdaysite.com</c> is CompanyAts).
    /// </summary>
    public static SiteChannel Classify(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return SiteChannel.Unknown;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return SiteChannel.Unknown;

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
            host = host[4..];

        // Longest-suffix match: a tenant subdomain inherits the board class.
        foreach (var (known, channel) in Hosts)
        {
            if (host == known || host.EndsWith("." + known, StringComparison.OrdinalIgnoreCase))
                return channel;
        }

        return SiteChannel.Unknown;
    }

    /// <summary>The gate the orchestration uses: only this channel may ever auto-submit.</summary>
    public static bool IsAutoSubmittable(string? url) =>
        Classify(url) == SiteChannel.CompanyAts;
}
