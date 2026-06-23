using Envoy.Discovery.Models;
using Envoy.Discovery.Sources;
using Envoy.GhostDetection.Models;
using Microsoft.Extensions.Logging;

namespace Envoy.Discovery;

/// <summary>
/// Aggregates job postings from sanctioned, public sources: ATS board APIs (read-only,
/// unauthenticated) and an official key-gated web-search API. No scraping, no anti-bot
/// evasion. Discovered postings are returned as <see cref="JobPosting"/> so the
/// ghost-detection scorer can rank them.
/// </summary>
public class JobDiscoveryService
{
    private const int MaxBoardConcurrency = 4;

    private readonly IReadOnlyDictionary<JobSource, IAtsBoardSource> _ats;
    private readonly IWebSearchSource _webSearch;
    private readonly ILogger<JobDiscoveryService> _log;

    public JobDiscoveryService(IEnumerable<IAtsBoardSource> sources, IWebSearchSource webSearch, ILogger<JobDiscoveryService> log)
    {
        _ats = sources.ToDictionary(s => s.Ats);
        _webSearch = webSearch;
        _log = log;
    }

    /// <summary>The out-of-the-box set of public boards to scan; user-editable via seed-boards.json.</summary>
    public IReadOnlyList<AtsBoardRef> DefaultBoards { get; } = SeedBoards.Load();

    /// <summary>ATS types this service can read.</summary>
    public IReadOnlyCollection<JobSource> SupportedAts => _ats.Keys.ToArray();

    /// <summary>Fetch every board in parallel (politely capped), then filter and rank locally.</summary>
    public async Task<DiscoveryResult> SearchBoardsAsync(IEnumerable<AtsBoardRef> boards, DiscoveryQuery query, CancellationToken ct = default)
    {
        var boardList = boards.ToList();
        var all = new List<JobPosting>();
        var errors = new List<string>();
        using var sem = new SemaphoreSlim(MaxBoardConcurrency);

        var tasks = boardList.Select(async b =>
        {
            await sem.WaitAsync(ct);
            try
            {
                if (!_ats.TryGetValue(b.Ats, out var source))
                {
                    lock (errors) errors.Add($"{b.Ats} {b.Token}: no source registered for this ATS");
                    return;
                }

                var jobs = await source.FetchBoardAsync(b.Token, b.CompanyName, ct);
                lock (all) all.AddRange(jobs);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Discovery board {Ats}/{Token} failed", b.Ats, b.Token);
                lock (errors) errors.Add($"{b.Ats} {b.Token}: {ex.Message}");
            }
            finally
            {
                sem.Release();
            }
        });

        await Task.WhenAll(tasks);

        return new DiscoveryResult
        {
            Jobs = ApplyFilter(all, query, requireKeywords: true),
            Errors = errors,
            TotalBeforeFilter = all.Count
        };
    }

    /// <summary>Run a custom query through the official web-search API (requires the user's key).</summary>
    public async Task<DiscoveryResult> WebSearchAsync(string apiKey, string query, DiscoveryQuery filter, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return new DiscoveryResult { Errors = new[] { "No Brave Search API key configured. Add one in LLM Nexus / settings to enable web search." } };
        if (string.IsNullOrWhiteSpace(query))
            return new DiscoveryResult { Errors = new[] { "Enter a search query." } };

        try
        {
            var results = await _webSearch.SearchAsync(apiKey, query, Math.Clamp(filter.MaxResults, 1, 20), ct);
            return new DiscoveryResult
            {
                Jobs = ApplyFilter(results.ToList(), filter, requireKeywords: false),
                TotalBeforeFilter = results.Count
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Brave web search failed");
            return new DiscoveryResult { Errors = new[] { $"Web search failed: {ex.Message}" } };
        }
    }

    private static IReadOnlyList<JobPosting> ApplyFilter(List<JobPosting> jobs, DiscoveryQuery q, bool requireKeywords)
    {
        IEnumerable<JobPosting> result = jobs;

        var terms = (q.Keywords ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requireKeywords && terms.Length > 0)
            result = result.Where(j => terms.All(t =>
                j.JobTitle.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                j.DescriptionText.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                j.Location.Contains(t, StringComparison.OrdinalIgnoreCase)));

        if (!string.IsNullOrWhiteSpace(q.Location))
            result = result.Where(j =>
                j.Location.Contains(q.Location!, StringComparison.OrdinalIgnoreCase) ||
                j.Location.Contains("remote", StringComparison.OrdinalIgnoreCase));

        if (q.RemoteOnly)
            result = result.Where(j => j.Location.Contains("remote", StringComparison.OrdinalIgnoreCase));

        return result
            .Where(j => !string.IsNullOrWhiteSpace(j.Url))
            .GroupBy(j => j.Url)
            .Select(g => g.First())
            .OrderByDescending(j => j.PostedAtUtc ?? DateTime.MinValue)
            .Take(Math.Max(1, q.MaxResults))
            .ToList();
    }
}
