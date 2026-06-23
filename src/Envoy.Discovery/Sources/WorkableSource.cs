using Envoy.Discovery.Internal;
using Envoy.GhostDetection.Models;
using System.Text.Json;

namespace Envoy.Discovery.Sources;

/// <summary>
/// Workable public careers widget — GET /api/v1/widget/accounts/{subdomain}?details=true
/// returns the account's jobs (with descriptions) in a single call. The ?details=true is
/// required; without it the jobs array comes back empty.
/// </summary>
public class WorkableSource : IAtsBoardSource
{
    private readonly HttpClient _http;
    public WorkableSource(HttpClient http) => _http = http;

    public JobSource Ats => JobSource.Workable;

    public async Task<IReadOnlyList<JobPosting>> FetchBoardAsync(string token, string? companyName, CancellationToken ct = default)
    {
        var url = $"https://apply.workable.com/api/v1/widget/accounts/{Uri.EscapeDataString(token)}?details=true";
        var jsonText = await _http.GetStringAsync(url, ct);

        var jobs = new List<JobPosting>();
        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;

        var company = companyName;
        if (string.IsNullOrWhiteSpace(company))
            company = Json.Str(root, "name");
        if (string.IsNullOrWhiteSpace(company))
            company = Naming.Prettify(token);

        if (!root.TryGetProperty("jobs", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return jobs;

        foreach (var j in arr.EnumerateArray())
        {
            try
            {
                var title = Json.Str(j, "title");
                if (string.IsNullOrWhiteSpace(title)) continue;

                var remote = Json.Bool(j, "telecommuting");
                var location = BuildLocation(j, remote);
                var published = Json.Str(j, "published_on");

                jobs.Add(new JobPosting
                {
                    Source = JobSource.Workable,
                    CompanyName = company!,
                    JobTitle = title,
                    Location = location,
                    DescriptionText = HtmlText.Strip(Json.Str(j, "description")),
                    Url = Json.Str(j, "url"),
                    PostedAtUtc = DateParsing.Iso(published),
                    RawSourceId = Json.Str(j, "shortcode")
                });
            }
            catch { /* skip malformed posting */ }
        }
        return jobs;
    }

    private static string BuildLocation(JsonElement j, bool remote)
    {
        var city = Json.Str(j, "city");
        var state = Json.Str(j, "state");
        var country = Json.Str(j, "country");
        var parts = new[] { city, state, country }.Where(p => !string.IsNullOrWhiteSpace(p));
        var loc = string.Join(", ", parts);
        if (remote)
            loc = string.IsNullOrWhiteSpace(loc) ? "Remote" : $"Remote · {loc}";
        return loc;
    }
}
