using Envoy.Core.Configuration;
using Envoy.Core.Models;

namespace Envoy.Core.Services;

/// <summary>
/// The Full-Auto policy (APP-12): a single, testable decision point for
/// "may this queue item submit without the human gate?". The rules, in one place:
/// 1. the master switch is ON,
/// 2. the SiteClassifier calls the board a COMPANY ATS (aggregators and US unknowns never pass),
/// 3. the host is trusted — three verified completions on that host, or an
///    explicit entry in the trust list,
/// 4. the optional UTC hour window admits the moment (overnight drain).
/// Verified-focused: a submission that couldn't verify does NOT count toward trust.
/// </summary>
public static class FullAutoPolicy
{
    /// <summary>Verified completions on a host before the queue may auto-submit it without the trust-list entry.</summary>
    public const int VerifiedTrustThreshold = 3;

    public static bool IsAllowed(
        string? jobUrl,
        EnvoySettings settings,
        IReadOnlyList<ApplicationLog> priorLogs,
        DateTime nowUtc)
    {
        if (!settings.FullAutoEnabled)
            return false;

        if (!SiteClassifier.IsAutoSubmittable(jobUrl))
            return false;

        if (!IsHostTrusted(jobUrl, settings, priorLogs))
            return false;

        if (!InWindow(settings.FullAutoWindowStartHour, settings.FullAutoWindowEndHour, nowUtc))
            return false;

        return true;
    }

    public static bool IsHostTrusted(string? jobUrl, EnvoySettings settings, IReadOnlyList<ApplicationLog> priorLogs)
    {
        var host = HostOf(jobUrl);
        if (host.Length == 0)
            return false;

        if (settings.FullAutoTrustedHosts.Any(h =>
            {
                var trusted = HostOf(h);
                return trusted.Length > 0 && string.Equals(trusted, host, StringComparison.OrdinalIgnoreCase);
            }))
            return true;

        var verifiedOnHost = priorLogs.Count(l =>
            l.Status == ApplicationStatus.Completed &&
            l.VerifiedSubmission == true &&
            string.Equals(HostOf(l.JobUrl), host, StringComparison.OrdinalIgnoreCase));

        return verifiedOnHost >= VerifiedTrustThreshold;
    }

    public static bool InWindow(int? startHour, int? endHour, DateTime nowUtc)
    {
        if (startHour is null && endHour is null)
            return true;
        if (startHour is null || endHour is null)
            return false; // half-open window is a misconfiguration: no drain

        var hour = nowUtc.Hour;
        if (startHour <= endHour)
            return hour >= startHour && hour < endHour;
        // Overnight wrap (e.g. 22 → 06)
        return hour >= startHour || hour < endHour;
    }

    public static string HostOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            // Trust-list entries are bare hosts ("boards.greenhouse.io" — that
            // is what the settings example documents), so a scheme-less string
            // is parsed with a synthetic scheme instead of being dropped.
            // (#92 Night-Owl diagnosis: without this, explicit trust lists
            // never matched and the drain could never admit a host.)
            && !Uri.TryCreate("https://" + url.Trim(), UriKind.Absolute, out uri))
            return string.Empty;

        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
}
