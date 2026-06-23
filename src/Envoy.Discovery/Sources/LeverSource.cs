using Envoy.Discovery.Internal;
using Envoy.GhostDetection.Models;
using System.Text.Json;

namespace Envoy.Discovery.Sources;

/// <summary>Lever public Postings API — one GET returns the company's active postings array.</summary>
public class LeverSource : IAtsBoardSource
{
    private readonly HttpClient _http;
    public LeverSource(HttpClient http) => _http = http;

    public JobSource Ats => JobSource.Lever;

    public async Task<IReadOnlyList<JobPosting>> FetchBoardAsync(string token, string? companyName, CancellationToken ct = default)
    {
        var company = string.IsNullOrWhiteSpace(companyName) ? Naming.Prettify(token) : companyName!;
        var url = $"https://api.lever.co/v0/postings/{Uri.EscapeDataString(token)}?mode=json";
        var jsonText = await _http.GetStringAsync(url, ct);

        var jobs = new List<JobPosting>();
        using var doc = JsonDocument.Parse(jsonText);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return jobs;

        foreach (var j in doc.RootElement.EnumerateArray())
        {
            try
            {
                var title = Json.Str(j, "text");
                if (string.IsNullOrWhiteSpace(title)) continue;

                var location = Json.TryObj(j, "categories", out var cat) ? Json.Str(cat, "location") : "";
                var desc = Json.Str(j, "descriptionPlain");
                if (string.IsNullOrWhiteSpace(desc))
                    desc = HtmlText.Strip(Json.Str(j, "description"));

                jobs.Add(new JobPosting
                {
                    Source = JobSource.Lever,
                    CompanyName = company,
                    JobTitle = title,
                    Location = location,
                    DescriptionText = desc,
                    Url = Json.Str(j, "hostedUrl"),
                    PostedAtUtc = DateParsing.UnixMs(Json.Long(j, "createdAt")),
                    SalaryText = LeverSalary(j),
                    RawSourceId = Json.Str(j, "id")
                });
            }
            catch { /* skip malformed posting */ }
        }
        return jobs;
    }

    private static string? LeverSalary(JsonElement j)
    {
        if (!Json.TryObj(j, "salaryRange", out var s)) return null;
        var min = Json.Long(s, "minValue");
        var max = Json.Long(s, "maxValue");
        if (min <= 0 && max <= 0) return null;
        var cur = Json.Str(s, "currency");
        return $"{(min > 0 ? min.ToString("N0") : "?")}–{(max > 0 ? max.ToString("N0") : "?")} {cur}".Trim();
    }
}
