using Envoy.Core.Services;
using Envoy.GhostDetection.Models;
using System.Text.Json;

namespace Envoy.GhostDetection.Signals;

/// <summary>
/// Deterministic signal: cross-check a job posting against the company's own
/// public ATS feed (Greenhouse or Lever). If the role is live on the aggregator
/// but absent or closed on the company's board, that's strong evidence the posting
/// may be a ghost.
/// </summary>
public class AtsCrossCheckSignal : IGhostSignal
{
    private readonly HttpClient _http;

    public string Name => "ATS Cross-Check";
    public SignalTier Tier => SignalTier.Deterministic;
    public bool RequiresNetwork => true;

    public AtsCrossCheckSignal(HttpClient http)
    {
        _http = http;
    }

    public async Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default)
    {
        var ats = ExtractAtsInfo(posting);
        if (ats == null)
            return null;

        try
        {
            var jobs = await QueryAtsAsync(ats, ct);
            var match = FindBestMatch(jobs, posting);

            if (match == null)
            {
                // If the ATS was inferred from the company name (not the posting URL),
                // a no-match is not actionable: the guessed slug might resolve to a
                // different company's real board. Returning null preserves precision.
                if (ats.IsInferred)
                    return null;

                return new SignalResult
                {
                    SignalName = Name,
                    Score = 0.85,
                    Confidence = 0.75,
                    Evidence = new[]
                    {
                        $"Listed on {posting.Source} but not found on {posting.CompanyName}'s {ats.AtsName} board."
                    },
                    Tier = Tier
                };
            }

            return new SignalResult
            {
                SignalName = Name,
                Score = 0.15,
                Confidence = 0.80,
                Evidence = new[]
                {
                    $"Confirmed live on {posting.CompanyName}'s own {ats.AtsName} board."
                },
                Tier = Tier
            };
        }
        catch (HttpRequestException)
        {
            return null; // ATS unreachable — no opinion
        }
        catch (TaskCanceledException)
        {
            return null; // timeout — no opinion
        }
        catch
        {
            return null; // parse error — no opinion
        }
    }

    // ── Extraction ─────────────────────────────────────────────────────

    private static AtsInfo? ExtractAtsInfo(JobPosting posting)
    {
        // Direct Greenhouse URL — known ATS, not inferred
        if (posting.Url.Contains("greenhouse.io", StringComparison.OrdinalIgnoreCase))
        {
            var token = ExtractGreenhouseBoardToken(posting.Url);
            if (!string.IsNullOrEmpty(token))
                return new AtsInfo("Greenhouse", token, null, false);
        }

        // Direct Lever URL — known ATS, not inferred
        if (posting.Url.Contains("lever.co", StringComparison.OrdinalIgnoreCase))
        {
            var company = ExtractLeverCompany(posting.Url);
            if (!string.IsNullOrEmpty(company))
                return new AtsInfo("Lever", null, company, false);
        }

        // Aggregator — inferred from company name; never emit a high-confidence
        // "not found" on a guess. If the guessed slug resolves to a different
        // company's board, we'd falsely flag a real job.
        if (posting.Source is JobSource.Indeed or JobSource.LinkedIn or JobSource.Other)
        {
            var companySlug = Slugify(posting.CompanyName);
            if (!string.IsNullOrEmpty(companySlug))
            {
                return new AtsInfo("Lever", null, companySlug, true);
            }
        }

        return null;
    }

    private static string? ExtractGreenhouseBoardToken(string url)
    {
        // https://boards.greenhouse.io/{token}/jobs/{id}
        try
        {
            var uri = new Uri(url);
            var segments = uri.Segments;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i].Trim('/').Equals("boards.greenhouse.io", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (segments[i].Trim('/').Equals("jobs", StringComparison.OrdinalIgnoreCase))
                    return null;
                var seg = segments[i].Trim('/');
                if (!string.IsNullOrEmpty(seg) && seg != "boards")
                    return seg;
            }
        }
        catch { }
        return null;
    }

    private static string? ExtractLeverCompany(string url)
    {
        // https://jobs.lever.co/{company}/{id}
        try
        {
            var uri = new Uri(url);
            var segments = uri.Segments;
            for (int i = 0; i < segments.Length; i++)
            {
                var seg = segments[i].Trim('/');
                if (seg.Equals("jobs.lever.co", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrEmpty(seg))
                    return seg;
            }
        }
        catch { }
        return null;
    }

    private static string Slugify(string companyName)
    {
        return companyName.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace(".", "")
            .Replace(",", "")
            .Replace("&", "and")
            .Replace("'", "")
            .Replace("(", "")
            .Replace(")", "");
    }

    // ── Query ──────────────────────────────────────────────────────────

    private async Task<List<AtsJob>> QueryAtsAsync(AtsInfo ats, CancellationToken ct)
    {
        if (ats.AtsName == "Greenhouse" && !string.IsNullOrEmpty(ats.BoardToken))
        {
            var url = $"https://boards-api.greenhouse.io/v1/boards/{Uri.EscapeDataString(ats.BoardToken)}/jobs";
            var json = await _http.GetStringAsync(url, ct);
            return ParseGreenhouseJobs(json);
        }

        if (ats.AtsName == "Lever" && !string.IsNullOrEmpty(ats.CompanySlug))
        {
            var url = $"https://api.lever.co/v0/postings/{Uri.EscapeDataString(ats.CompanySlug)}?mode=json";
            var json = await _http.GetStringAsync(url, ct);
            return ParseLeverJobs(json);
        }

        return new List<AtsJob>();
    }

    private static List<AtsJob> ParseGreenhouseJobs(string json)
    {
        var jobs = new List<AtsJob>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("jobs", out var jobsArray))
            {
                foreach (var j in jobsArray.EnumerateArray())
                {
                    var title = j.TryGetProperty("title", out var t) ? t.GetString() : "";
                    var location = "";
                    if (j.TryGetProperty("location", out var locEl) && locEl.TryGetProperty("name", out var locName))
                        location = locName.GetString() ?? "";
                    jobs.Add(new AtsJob(title ?? "", location));
                }
            }
        }
        catch { }
        return jobs;
    }

    private static List<AtsJob> ParseLeverJobs(string json)
    {
        var jobs = new List<AtsJob>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var j in doc.RootElement.EnumerateArray())
            {
                var title = j.TryGetProperty("text", out var t) ? t.GetString() : "";
                var location = "";
                if (j.TryGetProperty("categories", out var cat) && cat.TryGetProperty("location", out var loc))
                    location = loc.GetString() ?? "";
                jobs.Add(new AtsJob(title ?? "", location));
            }
        }
        catch { }
        return jobs;
    }

    // ── Matching ───────────────────────────────────────────────────────

    private static AtsJob? FindBestMatch(List<AtsJob> jobs, JobPosting posting)
    {
        AtsJob? best = null;
        double bestScore = -1;

        foreach (var job in jobs)
        {
            var titleSim = DomScorer.NormalizedSimilarity(posting.JobTitle, job.Title);
            var locSim = DomScorer.NormalizedSimilarity(posting.Location, job.Location);
            var score = titleSim * 0.7 + locSim * 0.3;

            if (score > bestScore)
            {
                bestScore = score;
                best = job;
            }
        }

        return bestScore >= 0.55 ? best : null;
    }

    // ── Internal models ──────────────────────────────────────────────────

    private record AtsInfo(string AtsName, string? BoardToken, string? CompanySlug, bool IsInferred);
    private record AtsJob(string Title, string Location);
}
