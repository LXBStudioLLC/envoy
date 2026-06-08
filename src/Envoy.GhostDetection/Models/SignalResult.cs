namespace Envoy.GhostDetection.Models;

public enum SignalTier
{
    /// <summary>Hard evidence (e.g. ATS says the job is closed).</summary>
    Deterministic,

    /// <summary>Strong statistical or correlational evidence.</summary>
    Probabilistic,

    /// <summary>Weak or noisy signal — contributes to evidence only, near-zero weight in scoring.</summary>
    Weak
}

public class SignalResult
{
    public string SignalName { get; set; } = string.Empty;
    public double Score { get; set; }
    public double Confidence { get; set; }
    public string[] Evidence { get; set; } = Array.Empty<string>();
    public SignalTier Tier { get; set; }
}
