using Envoy.Discovery.Internal;
using Envoy.GhostDetection.Models;
using System.Text.Json;

namespace Envoy.Discovery.Sources;

/// <summary>
/// Brave Search API web-search source. Official, key-gated (X-Subscription-Token),
/// CAPTCHA-free — the sanctioned front door for custom job queries. No scraping.
/// </summary>
public class BraveSearchSource : IWebSearchSource
{
    private readonly HttpClient _http;
    public BraveSearchSource(HttpClient http) => _http = http;

    public string Name => "Brave Search";

    public async Task<IReadOnlyList<JobPosting>> SearchAsync(string apiKey, string query, int count, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Brave Search API key is required.");

        count = Math.Clamp(count, 1, 20);

        // If the user didn't already include site: operators, augment the query to bias
        // toward ATS-hosted job boards. This makes web search return structured job
        // postings on known ATS domains rather than random web pages.
        var augmentedQuery = query;
        if (!augmentedQuery.Contains("site:", StringComparison.OrdinalIgnoreCase))
        {
            // Append site: operators so Brave prioritizes ATS-hosted boards. Brave Search
            // supports OR'd site: filters — this produces results dominated by actual
            // job postings on Greenhouse, Lever, Ashby, Workable, Recruitee, SmartRecruiters.
            augmentedQuery += " (site:boards.greenhouse.io OR site:jobs.lever.co OR site:api.ashbyhq.com OR site:apply.workable.com OR site:smartrecruiters.com)";
        }

        var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(augmentedQuery)}&count={count}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Subscription-Token", apiKey);
        req.Headers.Accept.ParseAdd("application/json");

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var jsonText = await resp.Content.ReadAsStringAsync(ct);

        var jobs = new List<JobPosting>();
        using var doc = JsonDocument.Parse(jsonText);
        if (!Json.TryObj(doc.RootElement, "web", out var web)) return jobs;
        if (!web.TryGetProperty("results", out var arr) || arr.ValueKind != JsonValueKind.Array) return jobs;

        foreach (var r in arr.EnumerateArray())
        {
            try
            {
                var title = Json.Str(r, "title");
                var link = Json.Str(r, "url");
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link)) continue;

                var host = Json.TryObj(r, "meta_url", out var meta) ? Json.Str(meta, "hostname") : "";

                jobs.Add(new JobPosting
                {
                    Source = JobSource.Other,
                    CompanyName = host,
                    JobTitle = title,
                    DescriptionText = HtmlText.Strip(Json.Str(r, "description")),
                    Url = link,
                    PostedAtUtc = DateParsing.Iso(Json.Str(r, "page_age"))
                });
            }
            catch { /* skip malformed result */ }
        }
        return jobs;
    }
}
