using Envoy.Discovery.Internal;
using Envoy.GhostDetection.Models;
using System.Text.Json;

namespace Envoy.Discovery.Sources;

/// <summary>Ashby public job-board posting API — one GET returns all published postings for a board.</summary>
public class AshbySource : IAtsBoardSource
{
    private readonly HttpClient _http;
    public AshbySource(HttpClient http) => _http = http;

    public JobSource Ats => JobSource.Ashby;

    public async Task<IReadOnlyList<JobPosting>> FetchBoardAsync(string token, string? companyName, CancellationToken ct = default)
    {
        var company = string.IsNullOrWhiteSpace(companyName) ? Naming.Prettify(token) : companyName!;
        var url = $"https://api.ashbyhq.com/posting-api/job-board/{Uri.EscapeDataString(token)}?includeCompensation=true";
        var jsonText = await _http.GetStringAsync(url, ct);

        var jobs = new List<JobPosting>();
        using var doc = JsonDocument.Parse(jsonText);
        if (!doc.RootElement.TryGetProperty("jobs", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return jobs;

        foreach (var j in arr.EnumerateArray())
        {
            try
            {
                var title = Json.Str(j, "title");
                if (string.IsNullOrWhiteSpace(title)) continue;

                var desc = Json.Str(j, "descriptionPlain");
                if (string.IsNullOrWhiteSpace(desc))
                    desc = HtmlText.Strip(Json.Str(j, "descriptionHtml"));

                var published = Json.Str(j, "publishedAt");

                jobs.Add(new JobPosting
                {
                    Source = JobSource.Ashby,
                    CompanyName = company,
                    JobTitle = title,
                    Location = Json.Str(j, "location"),
                    DescriptionText = desc,
                    Url = Json.Str(j, "jobUrl"),
                    PostedAtUtc = DateParsing.Iso(published),
                    SalaryText = AshbySalary(j),
                    RawSourceId = Json.Str(j, "id")
                });
            }
            catch { /* skip malformed posting */ }
        }
        return jobs;
    }

    private static string? AshbySalary(JsonElement j)
    {
        if (!Json.TryObj(j, "compensation", out var comp)) return null;
        var summary = Json.Str(comp, "scrapeableCompensationSalarySummary");
        if (string.IsNullOrWhiteSpace(summary))
            summary = Json.Str(comp, "compensationTierSummary");
        return string.IsNullOrWhiteSpace(summary) ? null : summary;
    }

    public async Task<bool> BoardExistsAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            var url = $"https://api.ashbyhq.com/posting-api/job-board/{Uri.EscapeDataString(token)}?limit=1";
            var jsonText = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(jsonText);
            return doc.RootElement.TryGetProperty("jobs", out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() >= 0;
        }
        catch { return false; }
    }
}
