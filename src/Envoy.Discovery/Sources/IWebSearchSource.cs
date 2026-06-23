using Envoy.GhostDetection.Models;

namespace Envoy.Discovery.Sources;

/// <summary>
/// An official, key-gated web-search API (e.g. Brave Search) used as a sanctioned
/// breadth layer for custom job queries. No scraping, no anti-bot evasion.
/// </summary>
public interface IWebSearchSource
{
    string Name { get; }

    /// <summary>Run a query and return result links shaped as <see cref="JobPosting"/> stubs.</summary>
    Task<IReadOnlyList<JobPosting>> SearchAsync(string apiKey, string query, int count, CancellationToken ct = default);
}
