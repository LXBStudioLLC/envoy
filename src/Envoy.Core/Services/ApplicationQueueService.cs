using Envoy.Core.Configuration;
using Envoy.Core.Models;
using Microsoft.Extensions.Logging;

namespace Envoy.Core.Services;

/// <summary>
/// Prepares one queued posting (implementable by the ApplicationOrchestrator;
/// testable with a stub so queue tests need no browser).
/// </summary>
public interface IApplicationPreparer
{
    /// <summary>Runs the no-scrape prepare for a queued item; returns the new TailoredProfile's id.</summary>
    Task<Guid> PrepareAsync(QueuedApplication item, CancellationToken ct);
}

/// <summary>
/// The application queue worker (APP-07): drains the PREPARE stage in the
/// background — tailoring + PDF for every Queued item that arrived from
/// discovery, with no browser usage at all — and marks each item AppReady for
/// the user's one-click fill. The fill itself stays human-gated exactly like
/// the single-apply flow (APP-11 wires the rapid review stream, APP-12 the
/// unattended mode; both build on this worker, not beside it).
///
/// <para>Single-flight by design: exactly one worker loop exists for the
/// process, started lazily on the first enqueue. The orchestrator it calls is
/// therefore never asked to run two flows at once, which is what makes the
/// shared CDP session safe.</para>
/// </summary>
public sealed class ApplicationQueueService : IDisposable
{
    private readonly IApplicationQueueRepository _queue;
    private readonly IApplicationPreparer _preparer;
    private readonly IApplicationSubmitter? _submitter;
    private readonly IFullAutoGate? _gate;
    private readonly Func<EnvoySettings>? _settings;
    private readonly ILogger<ApplicationQueueService> _log;

    private CancellationTokenSource? _workerCts;
    private volatile bool _running;

    private static readonly SemaphoreSlim PrepareSingleFlight = new(1, 1);

    /// <summary>Raised after the queue changes (enqueue, status transitions). UI subscribes and re-renders.</summary>
    public event Action? QueueChanged;

    public bool IsRunning => _running;

    public ApplicationQueueService(IApplicationQueueRepository queue, IApplicationPreparer preparer,
        ILogger<ApplicationQueueService> log, Func<EnvoySettings>? settings = null,
        IApplicationSubmitter? submitter = null, IFullAutoGate? gate = null)
    {
        _queue = queue;
        _preparer = preparer;
        _log = log;
        _settings = settings;
        _submitter = submitter;
        _gate = gate;
    }

    /// <summary>
    /// Adds a posting to the queue. The duplicate guard runs at enqueue time
    /// via <paramref name="duplicateCheck"/> (e.g. the completed-apply check
    /// from APP-04) and inside the queue itself (one active row per posting
    /// key). The per-company throttle (APP-11) refuses a second active row
    /// for the same company, and the configured daily cap bounds the batch.
    /// Returns false + a reason when blocked.
    /// </summary>
    private static readonly QueueItemStatus[] ActiveStatuses =
        { QueueItemStatus.Queued, QueueItemStatus.Preparing, QueueItemStatus.AppReady };

    private static string NormalizeCompany(string? company)
    {
        if (string.IsNullOrWhiteSpace(company)) return string.Empty;
        var sb = new System.Text.StringBuilder(company.Length);
        foreach (var ch in company.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public async Task<(bool Enqueued, string Reason)> EnqueueAsync(
        QueuedApplication item,
        Func<QueuedApplication, Task<string?>>? duplicateCheck = null,
        bool autoStart = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(item.PostingKey) || string.IsNullOrWhiteSpace(item.JobUrl))
            return (false, "The posting is missing its URL; this row cannot be queued.");

        if (!string.IsNullOrWhiteSpace(item.DescriptionText) && item.DescriptionText.Length < 120)
            return (false, "The posting's description is too short to tailor reliably; open it and apply manually.");

        // APP-11: one active application per company — the "same posting,
        // different role" corner is allowed only as separate rows that the
        // user reviews individually, but two active rows for one company with
        // identical post titles are just duplicates.
        var companyKey = NormalizeCompany(item.Company);
        if (companyKey.Length > 0)
        {
            var all = await _queue.GetAllAsync(ct);
            var activeSameCompany = all.Any(q => ActiveStatuses.Contains(q.Status)
                && NormalizeCompany(q.Company) == companyKey
                && q.PostingKey != item.PostingKey);
            if (activeSameCompany)
                return (false, $"{item.Company} already has an application in the queue (one active per company).");
        }

        if (_settings is not null)
        {
            var settings = _settings();
            if (settings.QueueDailyCap > 0)
            {
                var queueDay = DateTime.UtcNow.Date;
                var all = await _queue.GetAllAsync(ct);
                var enqueuedToday = all.Count(q =>
                    (ActiveStatuses.Contains(q.Status) || q.Status == QueueItemStatus.Submitted) &&
                    ((q.Status == QueueItemStatus.Submitted ? q.UpdatedAt : q.CreatedAt) ?? q.CreatedAt) >= queueDay);
                if (enqueuedToday >= settings.QueueDailyCap)
                    return (false, $"Daily cap reached ({settings.QueueDailyCap} queued today). The cap protects you from a velocity flag; it resets tomorrow.");
            }
        }

        if (duplicateCheck is not null)
        {
            var blockReason = await duplicateCheck(item);
            if (blockReason is not null)
            {
                // APP-11 audit fix: blocked rows are TRANSIENT reasons shown to
                // the user — persisting a fresh "ALREADY APPLIED" row on every
                // click accumulated duplicates in the panel.
                Notify();
                return (false, blockReason);
            }
        }

        if (!await _queue.AddAsync(item, ct))
            return (false, "This posting is already in the queue.");

        Notify();

        if (autoStart)
            EnsureStarted();

        return (true, "Queued.");
    }

    /// <summary>Lazily starts the single worker loop; safe to call repeatedly.</summary>
    public void EnsureStarted()
    {
        if (_running) return;

        _running = true;
        _workerCts = new CancellationTokenSource();
        _ = WorkerLoopAsync(_workerCts.Token);
        _log.LogInformation("Application queue worker started");
    }

    private async Task WorkerLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Multi-profile (APP-11 follow-up): rows carry their own
                // MasterProfileId stamp, so the worker tailors whichever row is
                // oldest — different profiles interleave instead of the first
                // profile's rows starving everyone else. The single-flight
                // prepare keeps the shared browser safe despite that.
                var item = await _queue.GetNextQueuedAnyProfileAsync(ct);
                if (item is null)
                    item = await DrainDueAsync(DateTime.UtcNow, ct); // Night Owl: parked rows drain when the window opens

                if (item is null)
                {
                    await WaitAsync(TimeSpan.FromSeconds(5), ct);
                    continue;
                }

                // Legacy rows queued before per-row stamping keep the old
                // behavior: bind to the first stamped row's profile. With no
                // stamped row to bind to, keep waiting (previous contract).
                if (item.MasterProfileId == Guid.Empty)
                {
                    var fallback = await ResolveProfileIdAsync();
                    if (fallback is null)
                    {
                        await WaitAsync(TimeSpan.FromSeconds(5), ct);
                        continue;
                    }
                    item.MasterProfileId = fallback.Value;
                }

                if (item.Status == QueueItemStatus.AppReady)
                {
                    // Night Owl row surfaced by the drain consult: already
                    // prepared and already gate-admitted — submit, never re-prepare.
                    await TryFullAutoSubmitAsync(item, DateTime.UtcNow, ct);
                    continue;
                }

                await ProcessItemAsync(item, DateTime.UtcNow, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown: normal
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Application queue worker crashed and stopped");
        }
        finally
        {
            _running = false;
            _log.LogInformation("Application queue worker stopped");
        }
    }

    /// <summary>Fallback for legacy unstamped rows: the profile of the first
    /// stamp-carrying row in the queue (empty stamps are skipped — otherwise an
    /// old row first in line would block every new stamped row again).</summary>
    private async Task<Guid?> ResolveProfileIdAsync()
    {
        var all = await _queue.GetAllAsync();
        var queued = all.FirstOrDefault(q => q.Status == QueueItemStatus.Queued && q.MasterProfileId != Guid.Empty)
            ?? all.FirstOrDefault(q => q.Status == QueueItemStatus.Preparing && q.MasterProfileId != Guid.Empty);
        return queued?.MasterProfileId is var id && id != Guid.Empty ? id : null;
    }

    /// <summary>
    /// One item's full worker pass (prepare → Full-Auto submit when the gate
    /// admits). Extracted so the night-window drain can be tested with an
    /// injected "now" — the worker passes DateTime.UtcNow.
    /// </summary>
    public async Task ProcessItemAsync(QueuedApplication item, DateTime nowUtc, CancellationToken ct)
    {
        await PrepareSingleFlight.WaitAsync(ct);
        try
        {
            item.Status = QueueItemStatus.Preparing;
            await _queue.UpdateAsync(item, ct);
            Notify();

            try
            {
                item.TailoredProfileId = await _preparer.PrepareAsync(item, ct);
                item.Status = QueueItemStatus.AppReady;
                item.Note = null;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Per-item cap (preparer's own timeout): honest failure.
                item.Status = QueueItemStatus.Failed;
                item.Note = "Tailoring timed out.";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                item.Status = QueueItemStatus.Failed;
                item.Note = ex.Message;
                _log.LogWarning(ex, "Queue prepare failed for {Url}", item.JobUrl);
            }

            if (item.Status == QueueItemStatus.AppReady && item.TailoredProfileId is not null
                && _gate is not null && _gate.ShouldAutoSubmitNow(item, nowUtc))
            {
                await TryFullAutoSubmitAsync(item, nowUtc, ct);
            }
            else
            {
                await _queue.UpdateAsync(item, ct);
                Notify();
            }
        }
        finally
        {
            PrepareSingleFlight.Release();
        }
    }

    private static async Task WaitAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Night-Owl drain (#92): the next parked AppReady row (oldest first) when
    /// the Full-Auto policy gate admits content at <paramref name="nowUtc"/>;
    /// the row is submitted and returned, so the caller can see what moved.
    /// Rows prepared outside the window (or before Full-Auto was enabled) drain
    /// the moment the gate says yes — no manual nudge, and never a re-prepare.
    /// Public + injectable "now" so the window contract is testable offline.
    /// </summary>
    public async Task<QueuedApplication?> DrainDueAsync(DateTime nowUtc, CancellationToken ct)
    {
        if (_submitter is null || _gate is null)
            return null;

        var all = await _queue.GetAllAsync(ct);
        var parked = all
            .Where(q => q.Status == QueueItemStatus.AppReady && q.TailoredProfileId is not null)
            .OrderBy(q => q.UpdatedAt ?? q.CreatedAt)
            .FirstOrDefault();
        if (parked is null)
            return null;

        if (!_gate.ShouldAutoSubmitNow(parked, nowUtc))
            return null;

        await TryFullAutoSubmitAsync(parked, nowUtc, ct);
        return parked;
    }

    /// <summary>Submits one prepared item without the human gate (the Full-Auto
    /// policy gate already consulted with the injected "now"). The submitter's
    /// returned ApplicationLog decides the honest queue outcome: Completed →
    /// Submitted; NeedsUserSubmit → NeedsUserSend (the human clicks the site's
    /// own Submit on this path); captcha → parked; everything else → Failed.</summary>
    private async Task TryFullAutoSubmitAsync(QueuedApplication item, DateTime nowUtc, CancellationToken ct)
    {
        try
        {
            var (resultLog, error, captcha) = await _submitter!.SubmitAsync(item, ct);
            if (resultLog is null)
            {
                item.Status = QueueItemStatus.Failed;
                item.Note = error;
            }
            else if (resultLog.Status == ApplicationStatus.Completed)
            {
                item.Status = QueueItemStatus.Submitted;
                item.Note = resultLog.VerifiedSubmission == true
                    ? "verified"
                    : "sent; delivery not verified";
            }
            else if (resultLog.Status == ApplicationStatus.RequiresCaptcha)
            {
                item.Status = QueueItemStatus.NeedsCaptcha;
                item.Note = "captcha during Full-Auto submit — finish in the browser, then requeue";
            }
            else if (resultLog.Status == ApplicationStatus.NeedsUserSubmit)
            {
                // The generic fill completed but there is no auto-click on
                // this path — the row parks for the user's hand instead of
                // lying in the submitted count.
                item.Status = QueueItemStatus.NeedsUserSend;
                item.Note = "filled; press the site's Submit in the browser";
            }
            else
            {
                item.Status = QueueItemStatus.Failed;
                item.Note = error ?? resultLog.ErrorMessage ?? resultLog.Status.ToString();
            }

            if (!string.IsNullOrWhiteSpace(resultLog?.VerificationNote) && string.IsNullOrWhiteSpace(item.Note))
                item.Note = resultLog.VerificationNote;

            await _queue.UpdateAsync(item, ct);
            Notify();
        }
        catch (Exception ex)
        {
            item.Status = QueueItemStatus.Failed;
            item.Note = ex.Message;
            await _queue.UpdateAsync(item, ct);
            Notify();
            _log.LogWarning(ex, "Full-Auto submit failed for {Url}", item.JobUrl);
        }
    }

    private void Notify() => QueueChanged?.Invoke();

    public void Dispose()
    {
        try
        {
            _workerCts?.Cancel();
            _workerCts?.Dispose();
        }
        catch { }
    }
}
