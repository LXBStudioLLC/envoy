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
    private readonly GenericFormFiller _genericFiller;
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
        GenericFormFiller genericFiller,
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
        _genericFiller = genericFiller;
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

    public async Task<PreparedApplication> PrepareApplicationAsync(Guid masterProfileId, string jobUrl, KnownPosting? knownPosting = null, CancellationToken ct = default)
    {
        var master = await _profileRepo.GetByIdAsync(masterProfileId, ct)
            ?? throw new InvalidOperationException("Master profile not found");

        // ── APP-02 fast path: the posting already came from discovery with its
        // description in hand — no scrape needed. One click → LLM tailoring,
        // no browser navigation at all in the prepare phase.
        var hasKnownJd = !string.IsNullOrWhiteSpace(knownPosting?.DescriptionText)
            && knownPosting!.DescriptionText.Length >= 120;

        // APP-03: the tailoring run is bound to the flow's cancel token.
        // local + cloud both observe it (linked CTS handles the timeout).
        string jobDescription;

        if (hasKnownJd)
        {
            jobDescription = knownPosting!.DescriptionText;
        }
        else
        {
            jobDescription = await ScrapeJobDescriptionAsync(jobUrl, ct);
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

        // APP-02: discovery already knows the company/title; the heuristic
        // extraction inside the tailor step is a fallback for postings we
        // surfaced ourselves.
        if (knownPosting is not null)
        {
            if (string.IsNullOrWhiteSpace(tailored.Company))
                tailored.Company = knownPosting.Company;
            if (string.IsNullOrWhiteSpace(tailored.JobTitle))
                tailored.JobTitle = knownPosting.JobTitle;
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

            // APP-10: a cover letter beside the resume when one was generated
            // (the user reads it there if the site's upload accepts two files).
            if (!string.IsNullOrWhiteSpace(tailored.CoverLetterText))
            {
                var letterPath = Path.ChangeExtension(pdfPath, ".cover-letter.txt");
                await File.WriteAllTextAsync(letterPath, tailored.CoverLetterText, ct);
            }
        }

        return new PreparedApplication(tailored, jobDescription);
    }

    /// <summary>
    /// Extracted from PrepareApplicationAsync: scrape a JD through the CDP
    /// browser (with the session-continuity guarantees of APP-03), returning
    /// "" on failure instead of failing the whole flow.
    /// </summary>
    private async Task<string> ScrapeJobDescriptionAsync(string jobUrl, CancellationToken ct)
    {
        var browserType = _browserLauncher.GetSelectedBrowserType() ?? BrowserType.Chrome;
        if (!await _browserLauncher.IsRunningWithDebuggingAsync(browserType))
        {
            var launched = await _browserLauncher.LaunchAsync(browserType);
            if (!launched)
            {
                throw new InvalidOperationException("Could not launch browser. Please ensure a supported browser is installed and try again.");
            }
        }

        string jobDescription = "";
        var connected = await _browser.ConnectAsync(ct: ct);

        if (!connected)
        {
            _log.LogWarning("Could not connect to browser for {JobUrl}; tailoring will run with empty job description", jobUrl);
            return jobDescription;
        }

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
            await _browser.CloseAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to fetch job description from {JobUrl}; tailoring will run with empty job description", jobUrl);
            if (targetId != null && _browser.IsConnected)
                await _browser.ClosePageAsync(targetId); // half-fetched page: don't hold it open
        }

        return jobDescription;
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
                // APP-09: no hand template → the generic field mapper covers the
                // page. Envoy fills what it safely can; the human completes the
                // rest and SENDS. Never invented values, never auto-submit.
                var generic = await _genericFiller.FillAsync(tailored, tailored.TailoredData, ct);

                log.SiteTemplateId = "generic-fill";
                log.RequiredFieldCount = generic.FieldCount;
                log.FilledFieldCount = generic.FilledCount;
                log.VerificationNote = generic.NeedsHuman.Count > 0
                    ? "needs human: " + string.Join("; ", generic.NeedsHuman.Take(8))
                    : "generic fill complete";

                log.Status = ApplicationStatus.NeedsUserSubmit;
                await _logRepo.UpdateAsync(log, ct);

                var summary = $"Auto-fill done: {generic.FilledCount} of {generic.FieldCount} fields filled." +
                    (generic.NeedsHuman.Count > 0
                        ? $" Finish these by hand: {string.Join(", ", generic.NeedsHuman.Take(6))}" +
                          (generic.NeedsHuman.Count > 6 ? $" (+{generic.NeedsHuman.Count - 6} more)" : "") + "."
                        : "") +
                    " Review the form in the browser and click the site's Submit yourself; nothing is sent by Envoy here.";
                await onConfirmationRequired(summary);

                return log;
            }

            log.SiteTemplateId = template.Id;

            // Execute template. The submit step is human-gated in EVERY mode: the
            // final click only fires after explicit approval. Stealth changes how
            // input is typed, never whether the human confirms the submission.
            var run = await _templates.ExecuteTemplateAsync(template, _browser, tailored, async msg =>
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

            // ── APP-05: honest completion ───────────────────────────────────
            log.RequiredFieldCount = run.RequiredTotal;
            log.FilledFieldCount = run.RequiredTotal - run.RequiredMissing;

            if (run.RequiredMissing > 0 || (run.RequiredTotal > 0 && run.SkippedCount > 0 && run.RequiredTotal == run.RequiredMissing))
            {
                // The engine skipped required fields (element missing, unknown
                // profile field, missing file). A "Completed" here would be a
                // lie; fail with what actually happened.
                log.Status = ApplicationStatus.Failed;
                log.ErrorMessage = $"Filled {run.RequiredTotal - run.RequiredMissing} of {run.RequiredTotal} required fields " +
                                   $"({run.SkippedCount} step(s) skipped). The page likely changed or the profile is missing data; " +
                                   "try again, or check the template/profile.";
                log.VerificationNote = "required-missing: " + run.RequiredMissing;

                // Final screenshot still valuable for diagnosis
                if (_settings.CaptureScreenshots)
                    log.AfterScreenshot = await _browser.CaptureScreenshotAsync(ct);
                return log;
            }

            // Final screenshot (honors the CaptureScreenshots opt-out)
            if (_settings.CaptureScreenshots)
                log.AfterScreenshot = await _browser.CaptureScreenshotAsync(ct);

            if (log.Status != ApplicationStatus.SafeModeStopped
                && log.Status != ApplicationStatus.DeclinedByUser)
            {
                log.Status = ApplicationStatus.Completed;
                log.VerifiedSubmission = run.VerifyPassed;
                if (run.VerifyNote is not null)
                    log.VerificationNote = run.VerifyNote;
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
