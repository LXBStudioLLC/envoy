using Envoy.Core.Models;

namespace Envoy.Core.Services;

/// <summary>
/// The duplicate-application guard (APP-04): a prior <c>Completed</c>
/// submission to the same posting blocks a silent re-submit. Posting identity
/// is canonicalized with <see cref="PostingKey"/> (host+path+query, tracking
/// noise stripped), with a same-normalized-company + same-title fallback that
/// catches the same posting reached via a different URL variant. Matching
/// prior attempts that did NOT complete (failed, captcha hand-off, declined)
/// never block — retrying those is the product's own advice.
///
/// <para>Pure and deterministic (no I/O): the orchestrator feeds it prior log
/// rows and the date; unit tests get full control. Whether an override is
/// offered is the caller's decision (the UI confirms, defaulting to no).</para>
/// </summary>
public static class DuplicateApplyCheck
{
    /// <summary>Canonical match: same posting identity.</summary>
    private const int ScoreSameKey = 2;

    /// <summary>Secondary match: same employer + same title (URL differs, e.g. a mirror board).</summary>
    private const int ScoreSameCompanyTitle = 1;

    public static (bool Blocked, string Message) Evaluate(
        IReadOnlyList<ApplicationLog> priorLogs,
        string? jobUrl,
        string? company,
        string? jobTitle,
        int windowDays,
        DateTime asOf)
    {
        var targetKey = PostingKey.For(jobUrl, company, jobTitle);
        var targetCompany = Normalize(company);
        var targetTitle = Normalize(jobTitle);

        ApplicationLog? prior = priorLogs
            .Where(IsCompleted)
            .Where(l => MatchScore(l) > 0)
            .OrderByDescending(l => NormalizeDate(l))
            .FirstOrDefault();

        if (prior is null)
            return (false, string.Empty);

        var priorAt = NormalizeDate(prior);
        var ageDays = windowDays > 0 ? (asOf - priorAt).TotalDays : double.MaxValue;
        var expiredByWindow = windowDays > 0 && ageDays > windowDays;

        if (expiredByWindow)
            return (false, string.Empty);

        var when = priorAt.ToString("yyyy-MM-dd");
        var windowNote = windowDays > 0
            ? $" A re-apply unlocks after {windowDays} day(s)."
            : " Envoy blocks re-applying until this is overridden.";
        var employer = string.IsNullOrWhiteSpace(prior.Company) ? string.Empty : $" Employer: {prior.Company.Trim()}.";
        var role = string.IsNullOrWhiteSpace(prior.JobTitle) ? string.Empty : $" Role: {prior.JobTitle.Trim()}.";

        return (true,
            $"You already completed an application for this posting on {when}.{windowNote}{employer}{role}");

        bool IsCompleted(ApplicationLog l) => l.Status == ApplicationStatus.Completed;

        int MatchScore(ApplicationLog l)
        {
            if (string.Equals(PostingKey.For(l.JobUrl, l.Company, l.JobTitle),
                    targetKey, StringComparison.Ordinal))
                return ScoreSameKey;
            if (targetCompany.Length > 0 &&
                Normalize(l.Company) == targetCompany &&
                targetTitle.Length > 0 &&
                Normalize(l.JobTitle) == targetTitle)
                return ScoreSameCompanyTitle;
            return 0;
        }

        static DateTime NormalizeDate(ApplicationLog l) =>
            (l.CompletedAt ?? l.StartedAt).Date;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        // Lowercase, strip punctuation, collapse whitespace: "Acme, Inc." and
        // "acme inc" are the same employer identity for the fallback match.
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        return string.Join(' ', sb.ToString()
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }
}
