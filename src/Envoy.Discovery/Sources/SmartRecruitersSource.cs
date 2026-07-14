using Envoy.Discovery.Internal;
using Envoy.GhostDetection.Models;
using System.Text.Json;

namespace Envoy.Discovery.Sources;

/// <summary>
/// SmartRecruiters public job posting API — one GET returns all published jobs
/// for a company via https://api.smartrecruiters.com/v1/companies/{slug}/jobs
/// </summary>
public class SmartRecruitersSource : IAtsBoardSource
{
    private readonly HttpClient _http;
    public SmartRecruitersSource(HttpClient http) => _http = http;

    public JobSource Ats => JobSource.SmartRecruiters;

    public async Task<IReadOnlyList<JobPosting>> FetchBoardAsync(string token, string? companyName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Array.Empty<JobPosting>();

        var company = string.IsNullOrWhiteSpace(companyName) ? Naming.Prettify(token) : companyName!;
        var allJobs = new List<JobPosting>();

        // The SmartRecruiters API returns 100 results per page. We paginate up to 10
        // pages (1000 jobs total) to support large companies.
        for (int offset = 0; offset < 1000; offset += 100)
        {
            var url = $"https://api.smartrecruiters.com/v1/companies/{Uri.EscapeDataString(token)}/jobs?offset={offset}&limit=100";
            string jsonText;

            try
            {
                jsonText = await _http.GetStringAsync(url, ct);
            }
            catch
            {
                // If the first page fails, return what we have (empty). If a later page
                // fails, return the jobs we collected from earlier pages.
                break;
            }

            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            if (!root.TryGetProperty("content", out var arr) || arr.ValueKind != JsonValueKind.Array)
                break;

            int count = 0;
            foreach (var j in arr.EnumerateArray())
            {
                try
                {
                    var title = Json.Str(j, "title");
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    var jobId = Json.Str(j, "id");
                    var jobUrl = $"https://jobs.smartrecruiters.com/{Uri.EscapeDataString(token)}/{jobId}";

                    // Location
                    var location = "";
                    if (Json.TryObj(j, "location", out var loc))
                    {
                        var city = Json.Str(loc, "city");
                        var country = Json.TryObj(loc, "country", out var c) ? Json.Str(c, "name") : "";
                        var parts = new[] { city, country }.Where(p => !string.IsNullOrWhiteSpace(p));
                        location = string.Join(", ", parts);
                    }
                    if (string.IsNullOrWhiteSpace(location) && Json.Bool(j, "remote"))
                        location = "Remote";

                    allJobs.Add(new JobPosting
                    {
                        Source = JobSource.SmartRecruiters,
                        CompanyName = company,
                        JobTitle = title,
                        Location = location,
                        DescriptionText = "", // The list endpoint doesn't include full descriptions; the
                                              // detail endpoint does, but that's one HTTP call per posting.
                                              // We leave it empty so the ghost scorer treats it as missing-data,
                                              // and the UI still shows title/company/location/score.
                        Url = jobUrl,
                        PostedAtUtc = DateParsing.Iso(Json.Str(j, "postedOn")),
                        LastUpdatedUtc = DateParsing.Iso(Json.Str(j, "updatedOn")),
                        RawSourceId = jobId
                    });
                    count++;
                }
                catch { /* skip malformed posting */ }
            }

            // Check if we've exhausted results (fewer than limit returned → last page)
            if (count < 100)
                break;
        }

        return allJobs;
    }

    public async Task<bool> BoardExistsAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            var url = $"https://api.smartrecruiters.com/v1/companies/{Uri.EscapeDataString(token)}/jobs?limit=1";
            using var resp = await _http.GetAsync(url, ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}