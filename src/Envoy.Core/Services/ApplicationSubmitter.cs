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
    Task<(bool Submitted, string? Error, bool Captcha)> SubmitAsync(QueuedApplication item, CancellationToken ct);
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

    public async Task<(bool Submitted, string? Error, bool Captcha)> SubmitAsync(QueuedApplication item, CancellationToken ct)
    {
        if (item.TailoredProfileId is null)
            return (false, "the item has no tailored profile; re-queue the posting", false);

        var log = await _orchestrator.SubmitApplicationAsync(
            item.TailoredProfileId.Value,
            ExecutionMode.FullAuto,
            _ => Task.FromResult(false), // the policy gate inside the orchestrator overrides; a second prompt path should not exist in Full-Auto
            ghostScore: item.GhostRiskScore is null
                ? null
                : new GhostScoreSnapshot(item.GhostRiskScore.Value, item.GhostRiskBand ?? "Neutral", Array.Empty<string>()),
            onDuplicateDetected: _ => Task.FromResult(false),
            ct: ct);

        return log.Status switch
        {
            ApplicationStatus.Completed or ApplicationStatus.NeedsUserSubmit => (true, null, false),
            ApplicationStatus.RequiresCaptcha => (false, "captcha hand-off", true),
            ApplicationStatus.DuplicateSkipped => (false, log.ErrorMessage, false),
            _ => (false, log.ErrorMessage ?? log.Status.ToString(), false)
        };
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
