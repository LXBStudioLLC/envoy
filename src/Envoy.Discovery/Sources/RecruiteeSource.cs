using Envoy.Discovery.Internal;
using Envoy.GhostDetection.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Envoy.Discovery.Sources;

/// <summary>
/// Recruitee public Careers Site API — GET https://{subdomain}.recruitee.com/api/offers/
/// lists all published offers in one call, no auth.
/// </summary>
public class RecruiteeSource : IAtsBoardSource
{
    private readonly HttpClient _http;
    public RecruiteeSource(HttpClient http) => _http = http;

    public JobSource Ats => JobSource.Recruitee;

    // The token is a DNS subdomain label; reject anything that isn't a safe hostname part.
    private static readonly Regex ValidSubdomain = new("^[a-z0-9][a-z0-9-]{0,61}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<IReadOnlyList<JobPosting>> FetchBoardAsync(string token, string? companyName, CancellationToken ct = default)
    {
        if (!ValidSubdomain.IsMatch(token))
            return Array.Empty<JobPosting>();

        var url = $"https://{token}.recruitee.com/api/offers/";
        var jsonText = await _http.GetStringAsync(url, ct);

        var jobs = new List<JobPosting>();
        using var doc = JsonDocument.Parse(jsonText);
        if (!doc.RootElement.TryGetProperty("offers", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return jobs;

        foreach (var j in arr.EnumerateArray())
        {
            try
            {
                var title = Json.Str(j, "title");
                if (string.IsNullOrWhiteSpace(title)) continue;

                var company = companyName;
                if (string.IsNullOrWhiteSpace(company))
                    company = Json.Str(j, "company_name");
                if (string.IsNullOrWhiteSpace(company))
                    company = Naming.Prettify(token);

                var desc = HtmlText.Strip(Json.Str(j, "description"));
                var published = Json.Str(j, "published_at");

                jobs.Add(new JobPosting
                {
                    Source = JobSource.Recruitee,
                    CompanyName = company!,
                    JobTitle = title,
                    Location = Json.Str(j, "location"),
                    DescriptionText = desc,
                    Url = Json.Str(j, "careers_url"),
                    PostedAtUtc = DateParsing.Recruitee(published),
                    RawSourceId = j.TryGetProperty("id", out var id) ? id.ToString() : null
                });
            }
            catch { /* skip malformed posting */ }
        }
        return jobs;
    }
}
