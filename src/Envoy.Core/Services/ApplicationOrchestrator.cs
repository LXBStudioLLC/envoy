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
            try
            {
                var targetId = await _browser.CreatePageAsync(ct);
                if (targetId != null)
                {
                    await _browser.AttachToPageAsync(targetId, ct);
                    await _browser.NavigateAsync(jobUrl, ct);
                    jobDescription = await _browser.GetPageTextAsync(ct);
                }
                else
                {
                    _log.LogWarning("Failed to create CDP page for {JobUrl}; tailoring will run with empty job description", jobUrl);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to fetch job description from {JobUrl}; tailoring will run with empty job description", jobUrl);
            }
            finally
            {
                await _browser.CloseAsync(CancellationToken.None);
            }
        }

        // Step 2: Tailor resume
        var tailored = await _tailoring.TailorAsync(masterProfileId, jobUrl, jobDescription, ct);

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
        CancellationToken ct = default)
    {
        var tailored = await _tailoredRepo.GetByIdAsync(tailoredProfileId, ct)
            ?? throw new InvalidOperationException($"Tailored profile {tailoredProfileId} not found.");

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

            // Capture before screenshot (honors the CaptureScreenshots opt-out)
            if (_settings.CaptureScreenshots)
                log.BeforeScreenshot = await _browser.CaptureScreenshotAsync(ct);

            // CAPTCHA check
            if (await _browser.DetectCaptchaAsync(ct))
            {
                log.Status = ApplicationStatus.RequiresCaptcha;
                await onConfirmationRequired("CAPTCHA detected. Please solve it manually in the browser window, then click Resume.");
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
