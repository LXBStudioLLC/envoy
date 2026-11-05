namespace Envoy.Core.Services;

/// <summary>
/// Lean, Core-owned posting data for the apply fast path (APP-02/APP-09):
/// when a posting arrives from discovery with its description already in
/// hand, tailoring can run with no scraping at all. Kept here (not as
/// GhostDetection's JobPosting) because Envoy.Core cannot depend on
/// Envoy.GhostDetection.
/// </summary>
public sealed record KnownPosting(
    string JobUrl,
    string Company,
    string JobTitle,
    string DescriptionText);
