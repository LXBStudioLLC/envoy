using Envoy.Discovery.Internal;
using Envoy.GhostDetection.Models;
using System.Text.Json;

namespace Envoy.Discovery.Sources;

/// <summary>Greenhouse public Job Board API — one GET returns every open posting for a board token.</summary>
public class GreenhouseSource : IAtsBoardSource
{
    private readonly HttpClient _http;
    public GreenhouseSource(HttpClient http) => _http = http;

    public JobSource Ats => JobSource.Greenhouse;

    public async Task<IReadOnlyList<JobPosting>> FetchBoardAsync(string token, string? companyName, CancellationToken ct = default)
    {
        var company = string.IsNullOrWhiteSpace(companyName) ? Naming.Prettify(token) : companyName!;
        var url = $"https://boards-api.greenhouse.io/v1/boards/{Uri.EscapeDataString(token)}/jobs?content=true";
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

                var location = Json.TryObj(j, "location", out var loc) ? Json.Str(loc, "name") : "";
                var updated = Json.Str(j, "updated_at");

                jobs.Add(new JobPosting
                {
                    Source = JobSource.Greenhouse,
                    CompanyName = company,
                    JobTitle = title,
                    Location = location,
                    DescriptionText = HtmlText.Strip(Json.Str(j, "content")),
                    Url = Json.Str(j, "absolute_url"),
                    PostedAtUtc = DateParsing.Iso(updated),
                    LastUpdatedUtc = DateParsing.Iso(updated),
                    RawSourceId = j.TryGetProperty("id", out var id) ? id.ToString() : null
                });
            }
            catch { /* skip malformed posting */ }
        }
        return jobs;
    }
}
