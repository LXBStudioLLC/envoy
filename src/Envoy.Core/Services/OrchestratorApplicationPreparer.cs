using Envoy.Core.Models;

namespace Envoy.Core.Services;

/// <summary>
/// Production adapter: runs the no-scrape prepare path on the real
/// orchestrator for one queued item. Kept tiny so the queue worker itself
/// stays unit-testable with a stubbed preparer.
/// </summary>
public sealed class OrchestratorApplicationPreparer : IApplicationPreparer
{
    private readonly ApplicationOrchestrator _orchestrator;

    public OrchestratorApplicationPreparer(ApplicationOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public async Task<Guid> PrepareAsync(QueuedApplication item, CancellationToken ct)
    {
        var prepared = await _orchestrator.PrepareApplicationAsync(
            item.MasterProfileId,
            item.JobUrl,
            new KnownPosting(item.JobUrl, item.Company, item.JobTitle, item.DescriptionText),
            ct);
        return prepared.Tailored.Id;
    }
}
