using Envoy.Discovery.Internal;
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
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IReadOnlyDictionary<JobSource, IAtsBoardSource> _ats;
    private readonly IWebSearchSource _webSearch;
    private readonly ILogger<JobDiscoveryService> _log;
    private readonly Dictionary<string, (DateTime Expiry, IReadOnlyList<JobPosting> Jobs)> _cache = new();

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
                // Check cache first
                var cacheKey = $"{b.Ats}:{b.Token}";
                if (_cache.TryGetValue(cacheKey, out var entry) && entry.Expiry > DateTime.UtcNow)
                {
                    lock (all) all.AddRange(entry.Jobs);
                    return;
                }

                if (!_ats.TryGetValue(b.Ats, out var source))
                {
                    lock (errors) errors.Add($"{b.Ats} {b.Token}: no source registered for this ATS");
                    return;
                }

                var jobs = await source.FetchBoardAsync(b.Token, b.CompanyName, ct);
                _cache[cacheKey] = (DateTime.UtcNow + CacheTtl, jobs);
                lock (all) all.AddRange(jobs);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // honor cancellation instead of recording it as a board failure
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

        var jobs = ApplyFilter(all, query, requireKeywords: true);
        // Enrich with a same-batch corpus so DuplicateJdSignal can compare each posting
        // against the others already fetched (sanctioned data in hand; no extra requests).
        DuplicateJdCorpus.Attach(jobs);
        return new DiscoveryResult
        {
            Jobs = jobs,
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
            var jobs = ApplyFilter(results.ToList(), filter, requireKeywords: false);
            DuplicateJdCorpus.Attach(jobs);
            return new DiscoveryResult
            {
                Jobs = jobs,
                TotalBeforeFilter = results.Count
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // honor cancellation instead of reporting it as a search failure
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
                j.Location.Contains(q.Location!, StringComparison.OrdinalIgnoreCase));

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

    /// <summary>
    /// Probe each registered ATS to find a public board for the given company name/token.
    /// Tries the slug as-is and with common transformations (lowercase, no spaces).
    /// Returns the first board that confirms it exists, or null if none found.
    /// </summary>
    public async Task<AtsBoardRef?> DiscoverBoardAsync(string companyName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(companyName)) return null;

        // Generate candidate slugs from the company name
        var baseName = companyName.Trim();
        var slug = baseName.ToLowerInvariant().Replace(" ", "").Replace("&", "").Replace("-", "").Replace(".", "");
        var slugWithDash = baseName.ToLowerInvariant().Replace(" ", "-").Replace("&", "");
        var slugNoSuffix = slug.Replace("inc", "").Replace("llc", "").Replace("corp", "").TrimEnd('-');

        var candidates = new[] { slug, slugWithDash, slugNoSuffix, baseName.ToLowerInvariant() }
            .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length >= 2)
            .Distinct()
            .ToList();

        foreach (var (ats, source) in _ats)
        {
            foreach (var candidate in candidates)
            {
                ct.ThrowIfCancellationRequested();
                if (await source.BoardExistsAsync(candidate, ct))
                {
                    return new AtsBoardRef
                    {
                        Ats = ats,
                        Token = candidate,
                        CompanyName = baseName
                    };
                }
            }
        }

        return null;
    }
}
