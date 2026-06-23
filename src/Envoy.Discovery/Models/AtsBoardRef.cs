using Envoy.GhostDetection.Models;

namespace Envoy.Discovery.Models;

/// <summary>Identifies one public ATS board to query (e.g. Greenhouse board token "openai").</summary>
public class AtsBoardRef
{
    public JobSource Ats { get; set; }
    public string Token { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
}
