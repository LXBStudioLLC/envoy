namespace Envoy.Discovery.Models;

/// <summary>Free-text filter applied client-side to discovered postings.</summary>
public class DiscoveryQuery
{
    public string? Keywords { get; set; }
    public string? Location { get; set; }
    public bool RemoteOnly { get; set; }
    public int MaxResults { get; set; } = 100;
}
