using Envoy.Core.Configuration;
using Envoy.Core.Models;
using Microsoft.Extensions.Logging;

namespace Envoy.Core.Services;

/// <summary>
/// Runs the pre-tailored submit for one queued item (APP-12). The
/// orchestrator is the production implementation: it consults the Full-Auto
/// policy again at gate time, records the ledger, captures the screenshots;
/// the worker only decides WHETHER the submit should be attempted.
/// </summary>
public interface IApplicationSubmitter
{
    /// <summary>Runs the pre-tailored submit; returns the resulting ApplicationLog
    /// (with terminal status) plus flags for captcha hand-offs from the worker.</summary>
    Task<(ApplicationLog? Log, string? Error, bool Captcha)> SubmitAsync(QueuedApplication item, CancellationToken ct);
}

/// <summary>The full-auto decision + execution adapter used by the queue worker.</summary>
public interface IFullAutoGate
{
    bool ShouldAutoSubmitNow(QueuedApplication item, DateTime nowUtc);
}

public sealed class OrchestratorApplicationSubmitter : IApplicationSubmitter
{
    private readonly ApplicationOrchestrator _orchestrator;

    public OrchestratorApplicationSubmitter(ApplicationOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public async Task<(ApplicationLog? Log, string? Error, bool Captcha)> SubmitAsync(QueuedApplication item, CancellationToken ct)
    {
        if (item.TailoredProfileId is null)
            return (null, "the item has no tailored profile; re-queue the posting", false);

        var log = await _orchestrator.SubmitApplicationAsync(
            item.TailoredProfileId.Value,
            ExecutionMode.FullAuto,
            _ => Task.FromResult(false), // the policy gate inside the orchestrator overrides; a second prompt path should not exist in Full-Auto
            ghostScore: item.GhostRiskScore is null
                ? null
                : new GhostScoreSnapshot(item.GhostRiskScore.Value, item.GhostRiskBand ?? "Neutral", Array.Empty<string>()),
            onDuplicateDetected: _ => Task.FromResult(false),
            ct: ct);

        // NeedsUserSubmit (generic fill, no auto click) is NOT a submission:
        // it parks the row for the user's hand without dishonoring the
        // submitted total. Submitted + captcha handled below.
        if (log.Status == ApplicationStatus.NeedsUserSubmit)
        {
            item.Note = null; // caller marks the queue row; keep call-sites uniform
        }

        // NeedsUserSubmit (generic fill, no auto click) is NOT a submission:
        // the queue marks the row for the user's hand instead of inflating
        // the submitted count. Captcha handoffs surface distinctly.
        return (log, log.ErrorMessage ?? log.Status.ToString(),
            log.Status == ApplicationStatus.RequiresCaptcha);
    }
}

/// <summary>The queue worker's Full-Auto release valve (APP-12).</summary>
public sealed class QueueFullAutoGate : IFullAutoGate
{
    private readonly IApplicationLogRepository _logs;
    private readonly Func<EnvoySettings> _settings;

    public QueueFullAutoGate(IApplicationLogRepository logs, Func<EnvoySettings> settings)
    {
        _logs = logs;
        _settings = settings;
    }

    public async Task<bool> ShouldAutoSubmitNowAsync(QueuedApplication item, DateTime nowUtc)
    {
        var settings = _settings();
        if (!settings.FullAutoEnabled || item.TailoredProfileId is null)
            return false;

        var prior = await _logs.GetAllWithoutScreenshotsAsync();
        return FullAutoPolicy.IsAllowed(item.JobUrl, settings, prior, nowUtc);
    }

    bool IFullAutoGate.ShouldAutoSubmitNow(QueuedApplication item, DateTime nowUtc)
        => ShouldAutoSubmitNowAsync(item, nowUtc).GetAwaiter().GetResult();
}
