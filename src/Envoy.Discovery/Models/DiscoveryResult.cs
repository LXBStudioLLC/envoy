using Envoy.GhostDetection.Models;

namespace Envoy.Discovery.Models;

/// <summary>The outcome of a discovery run: matched postings plus any per-source errors.</summary>
public class DiscoveryResult
{
    public IReadOnlyList<JobPosting> Jobs { get; init; } = Array.Empty<JobPosting>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public int TotalBeforeFilter { get; init; }
}
