using Envoy.Core.Configuration;
using Envoy.Core.Models;
using Microsoft.Extensions.Logging;

namespace Envoy.Core.Services;

public class ApplicationOrchestrator
{
    private readonly ResumeParserService _parser;
    private readonly TailoringEngine _tailoring;
    private readonly CdpBrowserService _browser;
    private readonly TemplateEngine _templates;
    private readonly IResumePdfGenerator _pdfGenerator;
    private readonly IProfileRepository _profileRepo;
    private readonly ITailoredProfileRepository _tailoredRepo;
    private readonly IApplicationLogRepository _logRepo;
    private readonly IJobEventRepository _eventRepo;
    private readonly IBrowserLauncher _browserLauncher;
    private readonly EnvoySettings _settings;
    private readonly ILogger<ApplicationOrchestrator> _log;

    public ApplicationOrchestrator(
        ResumeParserService parser,
        TailoringEngine tailoring,
        CdpBrowserService browser,
        TemplateEngine templates,
        IResumePdfGenerator pdfGenerator,
        IProfileRepository profileRepo,
        ITailoredProfileRepository tailoredRepo,
        IApplicationLogRepository logRepo,
        IJobEventRepository eventRepo,
        IBrowserLauncher browserLauncher,
        EnvoySettings settings,
        ILogger<ApplicationOrchestrator> log)
    {
        _parser = parser;
        _tailoring = tailoring;
        _browser = browser;
        _templates = templates;
        _pdfGenerator = pdfGenerator;
        _profileRepo = profileRepo;
        _tailoredRepo = tailoredRepo;
        _logRepo = logRepo;
        _eventRepo = eventRepo;
        _browserLauncher = browserLauncher;
        _settings = settings;
        _log = log;
    }

    public async Task<MasterProfile> ImportResumeAsync(string pdfPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pdfPath))
            throw new ArgumentException("Resume path is empty.", nameof(pdfPath));
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException($"Resume not found at {pdfPath}", pdfPath);
        if (!pdfPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only .pdf resumes are supported.", nameof(pdfPath));

        var profile = await _parser.ParseAsync(pdfPath, ct);
        await _profileRepo.AddAsync(profile, ct);
        return profile;
    }

    /// <summary>
    /// A prepared page held open from the prepare phase so the submit phase
    /// can reuse it (APP-03 session continuity). Canonical URL key guards the
    /// reuse; cleared after consumption, abandonment, or captcha hand-off.
    /// </summary>
    private sealed record PreparedPage(string CanonicalKey, string TargetId);
    private PreparedPage? _preparedPage;

    private static string CanonicalFor(string? url) =>
        PostingKey.For(url, null, null);

    private async Task DisposePreparedPageAsync()
    {
        var page = _preparedPage;
        _preparedPage = null;
        if (page != null && _browser.IsConnected)
            await _browser.ClosePageAsync(page.TargetId);
    }

    /// <summary>Drops the recorded page reference without closing the browser page.
    /// Used on the captcha hand-off, where the user must keep that page open.</summary>
    private void ForgetPreparedPage() => _preparedPage = null;

    public async Task<PreparedApplication> PrepareApplicationAsync(Guid masterProfileId, string jobUrl, CancellationToken ct = default)
    {
        var master = await _profileRepo.GetByIdAsync(masterProfileId, ct)
            ?? throw new InvalidOperationException("Master profile not found");

        // Ensure browser is running with debugging
        var browserType = _browserLauncher.GetSelectedBrowserType() ?? BrowserType.Chrome;
        if (!await _browserLauncher.IsRunningWithDebuggingAsync(browserType))
        {
            var launched = await _browserLauncher.LaunchAsync(browserType);
            if (!launched)
            {
                throw new InvalidOperationException("Could not launch browser. Please ensure a supported browser is installed and try again.");
            }
        }

        // Step 1: Extract job description via CDP
        string jobDescription = "";
        var connected = await _browser.ConnectAsync(ct: ct);

        if (!connected)
        {
            _log.LogWarning("Could not connect to browser for {JobUrl}; tailoring will run with empty job description", jobUrl);
        }
        else
        {
            string? targetId = null;
            try
            {
                targetId = await _browser.CreatePageAsync(ct);
                if (targetId != null)
                {
                    await _browser.AttachToPageAsync(targetId, ct);
                    await _browser.NavigateAsync(jobUrl, ct);
                    jobDescription = await _browser.GetPageTextAsync(ct);

                    // APP-03: keep the page + the connection for the submit
                    // phase. This saves a second full page navigation per
                    // application; the page is consumed or cleaned up there.
                    _preparedPage = new PreparedPage(CanonicalFor(jobUrl), targetId);
                }
                else
                {
                    _log.LogWarning("Failed to create CDP page for {JobUrl}; tailoring will run with empty job description", jobUrl);
                }
            }
            catch (OperationCanceledException)
            {
                // User cancel mid-navigation: mirror the WS-close behavior the
                // old flow had, but still avoid leaving an orphan page behind.
                if (targetId != null)
                    await _browser.ClosePageAsync(targetId);
                throw;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to fetch job description from {JobUrl}; tailoring will run with empty job description", jobUrl);
                if (targetId != null && _browser.IsConnected)
                    await _browser.ClosePageAsync(targetId); // half-fetched page: don't hold it open
            }
        }

        // Step 2: Tailor resume
        TailoredProfile tailored;
        try
        {
            tailored = await _tailoring.TailorAsync(masterProfileId, jobUrl, jobDescription, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // User cancel mid-tailoring: clean up the held page and surface.
            await DisposePreparedPageAsync();
            throw;
        }

        // Step 3: Generate PDF
        if (tailored.SafetyResult.Passed)
        {
            var pdfBytes = _pdfGenerator.Generate(tailored);
            // Shared with TemplateEngine (the upload reader) so the paths always match;
            // the untrusted Company/JobTitle are sanitized inside ResumeFilePath.
            var pdfPath = ResumeFilePath.For(tailored.TailoredData.Name, tailored.Company, tailored.JobTitle);
            Directory.CreateDirectory(Path.GetDirectoryName(pdfPath)!);
            await File.WriteAllBytesAsync(pdfPath, pdfBytes, ct);
        }

        return new PreparedApplication(tailored, jobDescription);
    }

    public async Task<ApplicationLog> SubmitApplicationAsync(
        Guid tailoredProfileId,
        ExecutionMode mode,
        Func<string, Task<bool>> onConfirmationRequired,
        GhostScoreSnapshot? ghostScore = null,
        Func<string, Task<bool>>? onDuplicateDetected = null,
        CancellationToken ct = default)
    {
        var tailored = await _tailoredRepo.GetByIdAsync(tailoredProfileId, ct)
            ?? throw new InvalidOperationException($"Tailored profile {tailoredProfileId} not found.");

        // ── APP-04 duplicate guard ──────────────────────────────────────
        // Consult the ledger BEFORE any browser work: a completed submission
        // to the same posting is a blocked re-submit unless the user,
        // explicitly prompted, says otherwise. Nothing here touches the
        // browser, so a duplicate never even launches.
        var priorLogs = await _logRepo.GetAllWithoutScreenshotsAsync(ct);
        var (blocked, duplicateMessage) = DuplicateApplyCheck.Evaluate(
            priorLogs, tailored.JobUrl, tailored.Company, tailored.JobTitle,
            _settings.DuplicateApplyWindowDays, DateTime.UtcNow);

        if (blocked)
        {
            var overrideConfirmed = onDuplicateDetected is not null
                && await onDuplicateDetected(duplicateMessage);

            if (!overrideConfirmed)
            {
                var skipLog = new ApplicationLog
                {
                    TailoredProfileId = tailoredProfileId,
                    JobUrl = tailored.JobUrl,
                    JobTitle = tailored.JobTitle,
                    Company = tailored.Company,
                    Mode = mode,
                    Status = ApplicationStatus.DuplicateSkipped,
                    ErrorMessage = duplicateMessage,
                    GhostRiskScore = ghostScore?.RiskScore,
                    GhostRiskBand = ghostScore?.Band,
                    StartedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow
                };
                await _logRepo.AddAsync(skipLog, ct);
                _log.LogInformation("Duplicate submission to {Url} blocked ({Message})", tailored.JobUrl, duplicateMessage);
                return skipLog;
            }

            _log.LogInformation("Duplicate detected for {Url} but the user chose to apply anyway", tailored.JobUrl);
        }

        var log = new ApplicationLog
        {
            TailoredProfileId = tailoredProfileId,
            JobUrl = tailored.JobUrl,
            JobTitle = tailored.JobTitle,
            Company = tailored.Company,
            Mode = mode,
            Status = ApplicationStatus.InProgress,
            GhostRiskScore = ghostScore?.RiskScore,
            GhostRiskBand = ghostScore?.Band
        };

        await _logRepo.AddAsync(log, ct);

        try
        {
            // Ensure browser is running
            var browserType = _browserLauncher.GetSelectedBrowserType() ?? BrowserType.Chrome;
            if (!await _browserLauncher.IsRunningWithDebuggingAsync(browserType))
            {
                var launched = await _browserLauncher.LaunchAsync(browserType);
                if (!launched)
                {
                    log.Status = ApplicationStatus.Failed;
                    log.ErrorMessage = "Could not launch browser. Please ensure a supported browser is installed.";
                    await _logRepo.UpdateAsync(log, ct);
                    return log;
                }
            }

            // Safety check: anomalies trigger Safe Mode
            if (mode == ExecutionMode.Stealth && (tailored.SafetyResult?.Passed == false))
            {
                log.Mode = ExecutionMode.Safe;
                log.Status = ApplicationStatus.SafeModeStopped;
                await onConfirmationRequired("Safety check failed. Switching to Safe Mode. Please review the tailored resume before proceeding.");
                await _logRepo.UpdateAsync(log, ct);
                return log;
            }

            // ── Browser page acquisition (APP-03 session continuity) ────────
            var canonicalUrl = CanonicalFor(tailored.JobUrl);
            var reusePage = _preparedPage is not null
                && _browser.IsConnected
                && string.Equals(_preparedPage.CanonicalKey, canonicalUrl, StringComparison.Ordinal);

            if (reusePage)
            {
                _log.LogInformation("Reusing the prepared page for {Url} (no second navigation)", tailored.JobUrl);
            }
            else
            {
                // A stale or different-URL prepared page is abandoned: close it
                // so we don't orphan browser tabs, then take the fresh path.
                await DisposePreparedPageAsync();
            }

            if (!reusePage)
            {
                if (!await _browser.ConnectAsync(ct: ct))
                {
                    log.Status = ApplicationStatus.Failed;
                    log.ErrorMessage = "Could not connect to Chrome.";
                    await _logRepo.UpdateAsync(log, ct);
                    return log;
                }

                var targetId = await _browser.CreatePageAsync(ct);
                if (targetId != null)
                    await _browser.AttachToPageAsync(targetId, ct);

                await _browser.NavigateAsync(tailored.JobUrl, ct);
            }

            // Capture before screenshot (honors the CaptureScreenshots opt-out)
            if (_settings.CaptureScreenshots)
                log.BeforeScreenshot = await _browser.CaptureScreenshotAsync(ct);

            // CAPTCHA check
            if (await _browser.DetectCaptchaAsync(ct))
            {
                log.Status = ApplicationStatus.RequiresCaptcha;
                await onConfirmationRequired("CAPTCHA detected. Envoy doesn't solve those. Finish it in the browser, then run the application again.");
                await _logRepo.UpdateAsync(log, ct);
                return log;
            }

            // Match template
            var template = _templates.MatchTemplate(tailored.JobUrl);
            if (template == null)
            {
                log.Status = ApplicationStatus.Failed;
                log.ErrorMessage = "No template found for this job board.";
                await _logRepo.UpdateAsync(log, ct);
                return log;
            }

            log.SiteTemplateId = template.Id;

            // Execute template. The submit step is human-gated in EVERY mode: the
            // final click only fires after explicit approval. Stealth changes how
            // input is typed, never whether the human confirms the submission.
            await _templates.ExecuteTemplateAsync(template, _browser, tailored, async msg =>
            {
                log.Status = ApplicationStatus.SafeModeStopped;
                await _logRepo.UpdateAsync(log, ct);

                var approved = await onConfirmationRequired(msg);
                // A "no" at the gate is a deliberate decision by a human who just
                // read the evidence — record it as its own status, distinct from
                // the safety-check auto-halt that shares this callback shape.
                log.Status = approved
                    ? ApplicationStatus.InProgress
                    : ApplicationStatus.DeclinedByUser;
                await _logRepo.UpdateAsync(log, ct);
                return approved;
            }, ct);

            // Final screenshot (honors the CaptureScreenshots opt-out)
            if (_settings.CaptureScreenshots)
                log.AfterScreenshot = await _browser.CaptureScreenshotAsync(ct);

            if (log.Status != ApplicationStatus.SafeModeStopped
                && log.Status != ApplicationStatus.DeclinedByUser)
            {
                log.Status = ApplicationStatus.Completed;
            }
        }
        catch (Exception ex)
        {
            log.Status = ApplicationStatus.Failed;
            log.ErrorMessage = ex.Message;
        }
        finally
        {
            // Don't close the page on a captcha hand-off: the user still needs
            // it open in the browser to finish by hand. In every other case
            // the page is consumed (or abandoned) and can be closed.
            if (log.Status == ApplicationStatus.RequiresCaptcha)
            {
                ForgetPreparedPage();
            }
            else
            {
                try { await DisposePreparedPageAsync(); }
                catch { /* cleanup must never mask the real outcome */ }
            }

            log.CompletedAt = DateTime.UtcNow;
            await _logRepo.UpdateAsync(log, CancellationToken.None);
            await _browser.CloseAsync(CancellationToken.None);
        }

        await RecordLedgerEventAsync(log, ghostScore);

        return log;
    }

    // The scoreboard ledger is bookkeeping — failing to record it must never
    // change the outcome of a submit flow that already ran.
    private async Task RecordLedgerEventAsync(ApplicationLog log, GhostScoreSnapshot? ghostScore)
    {
        try
        {
            var jobEvent = JobEvent.FromApplication(log, ghostScore);
            if (jobEvent != null)
                await _eventRepo.AddAsync(jobEvent, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to record scoreboard event for application {LogId}", log.Id);
        }
    }
}
