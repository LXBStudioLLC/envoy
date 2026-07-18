namespace Envoy.Core.Models;

/// <summary>
/// A ghost-detection result at the moment the user acted on a posting, carried
/// into Envoy.Core without a project reference to Envoy.GhostDetection. The
/// band travels as text ("Neutral" / "Elevated" / "High") because it outlives
/// the process in the database, where an enum renumbering must not re-label it.
/// </summary>
public record GhostScoreSnapshot(double RiskScore, string Band, string[] TopEvidence);
