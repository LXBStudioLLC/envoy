namespace Envoy.GhostDetection.Models;

public enum RiskBand
{
    /// <summary>No strong ghost signals found. The posting appears normal.</summary>
    Neutral,

    /// <summary>Multiple converging signals suggest caution, but no single hard proof.</summary>
    Elevated,

    /// <summary>Deterministic evidence indicates this posting is likely a waste of time.</summary>
    High
}

public class GhostScore
{
    /// <summary>Aggregated risk score from 0 (safe) to 100 (high risk).</summary>
    public double RiskScore { get; set; }

    public RiskBand Band { get; set; }

    public SignalResult[] Signals { get; set; } = Array.Empty<SignalResult>();

    /// <summary>Human-readable top evidence lines for display in the UI.</summary>
    public string[] TopEvidence { get; set; } = Array.Empty<string>();
}
