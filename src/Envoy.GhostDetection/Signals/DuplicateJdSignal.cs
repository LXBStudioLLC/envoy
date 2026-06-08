using Envoy.GhostDetection.Models;

namespace Envoy.GhostDetection.Signals;

/// <summary>
/// <strong>TODO:</strong> Detect near-identical job descriptions posted by unrelated companies.
///
/// <para>Approach: use <strong>local embeddings only</strong> (Ollama/LM Studio) — no cloud
/// API calls. Compute a cosine-similarity threshold between the current posting's
/// description and a sliding window of recently-seen descriptions. If similarity
/// exceeds ~0.85 across unrelated companies, that's evidence of copy-paste ghosting
/// or template farming.</para>
///
/// <para>Data source: the posting's own <see cref="JobPosting.DescriptionText"/> plus
/// a local in-memory or SQLite cache of previously-analyzed postings (not cloud).</para>
///
/// <para>Acceptance criteria: implements <see cref="IGhostSignal"/>, returns null when
/// no comparable postings exist, has fixture tests, no network calls in tests.</para>
/// </summary>
public class DuplicateJdSignal : IGhostSignal
{
    public string Name => "Duplicate JD";
    public SignalTier Tier => SignalTier.Probabilistic;

    public Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default)
    {
        // TODO: compute local embedding of DescriptionText, compare against cache,
        // return SignalResult when high similarity with unrelated company is found.
        return Task.FromResult<SignalResult?>(null);
    }
}
