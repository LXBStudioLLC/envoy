using Envoy.GhostDetection.Models;

namespace Envoy.GhostDetection;

/// <summary>
/// A single ghost-detection signal. Implementations may query public APIs,
/// perform local analysis, or compare against known patterns.
/// </summary>
public interface IGhostSignal
{
    /// <summary>Human-readable name for this signal.</summary>
    string Name { get; }

    /// <summary>Classification of how strong / conclusive this signal is.</summary>
    SignalTier Tier { get; }

    /// <summary>
    /// Evaluate a job posting. Return <c>null</c> when the signal has no opinion
    /// (e.g. data source unavailable, unsupported board, or insufficient information).
    /// </summary>
    Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default);
}
