using Envoy.Core.Configuration;
using Envoy.Core.Models;
using Envoy.Core.Services;
using Envoy.GhostDetection;
using Envoy.GhostDetection.Models;
using System.Windows;
using System.Windows.Controls;
using static Envoy.UI.Theme;

namespace Envoy.UI;

public partial class ApplyView : UserControl
{
    private readonly ApplicationOrchestrator _orchestrator;
    private readonly GhostScorer _ghostScorer;
    private readonly EnvoySettings _settings;
    private readonly IProfileRepository _profiles;
    private readonly ITailoredProfileRepository _tailoredRepo;
    private Guid _profileId;
    private TailoredProfile? _tailored;
    private string _jobDescription = "";
    private GhostScore? _ghostScore;
    private TaskCompletionSource<bool>? _confirmTcs;
    private CancellationTokenSource? _flowCts;
    private bool _tailoringInFlight;
    private bool _flowRunning;

    /// <summary>APP-02: set when the posting arrived from discovery, so tailoring
    /// runs on the already-known description instead of re-scraping.</summary>
    private KnownPosting? _discoveryPosting;

    public ApplyView(ApplicationOrchestrator orchestrator, GhostScorer ghostScorer, EnvoySettings settings,
        IProfileRepository profiles, ITailoredProfileRepository tailoredRepo)
    {
        _orchestrator = orchestrator;
        _ghostScorer = ghostScorer;
        _settings = settings;
        _profiles = profiles;
        _tailoredRepo = tailoredRepo;
        InitializeComponent();
        Loaded += (_, _) => RefreshModeOptions();
    }

    // ── APP-07: open an AppReady queue item for review ───────────────────

    /// <summary>
    /// A prepared, review-ready application from the queue: tailored profile
    /// loaded straight from the store (no LLM, no scrape), carried ghost
    /// score displayed, result panel ready — the user just hits FILL.
    /// </summary>
    public void LoadPreparedApplication(Guid tailoredProfileId, GhostScoreSnapshot? snapshot)
    {
        if (_flowRunning)
        {
            StatusText.Text = "A flow is already running. Wait for it (or cancel) first.";
            StatusText.Foreground = Yellow;
            return;
        }

        _ = LoadPreparedApplicationCoreAsync(tailoredProfileId, snapshot);
    }

    private async Task LoadPreparedApplicationCoreAsync(Guid tailoredProfileId, GhostScoreSnapshot? snapshot)
    {
        try
        {
            var tailored = await _tailoredRepo.GetByIdAsync(tailoredProfileId);
            if (tailored is null)
            {
                StatusText.Text = "This queued application's tailored profile is gone — re-queue the posting.";
                StatusText.Foreground = Red;
                return;
            }

            SetProfileId(tailored.MasterProfileId); // full reset first
            _tailored = tailored;
            _jobDescription = tailored.JobDescriptionText ?? string.Empty;
            TxtJobUrl.Text = tailored.JobUrl;

            if (snapshot is not null && Enum.TryParse<RiskBand>(snapshot.Band, out var band))
            {
                _ghostScore = new GhostScore
                {
                    RiskScore = snapshot.RiskScore,
                    Band = band,
                    TopEvidence = snapshot.TopEvidence ?? Array.Empty<string>()
                };
                ShowGhostSnapshot(_ghostScore);
            }

            ShowResult();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Something went wrong: {ex.Message}";
            StatusText.Foreground = Red;
        }
    }

    // Only offers the Stealth execution mode when stealth input is enabled (the guarded
    // opt-in from the Browser view); otherwise Safe is the only option. APP-16:
    // DefaultMode is now actually honored — the selected default moves the combo.
    private void RefreshModeOptions()
    {
        CmbMode.Items.Clear();
        CmbMode.Items.Add(new ComboBoxItem { Content = "Safe" });
        if (_settings.StealthModeEnabled)
            CmbMode.Items.Add(new ComboBoxItem { Content = "Stealth" });
        if (_settings.FullAutoEnabled)
            CmbMode.Items.Add(new ComboBoxItem { Content = "Full-Auto" });

        var wanted = _settings.DefaultMode switch
        {
            ExecutionMode.Stealth when _settings.StealthModeEnabled => "Stealth",
            ExecutionMode.FullAuto when _settings.FullAutoEnabled => "Full-Auto",
            _ => "Safe"
        };

        var match = CmbMode.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Content?.ToString() == wanted);
        if (match is not null)
        {
            CmbMode.SelectedItem = match;
        }
    }

    public void SetProfileId(Guid profileId)
    {
        _profileId = profileId;
        _tailored = null;
        _jobDescription = "";
        _ghostScore = null;
        _discoveryPosting = null;
        // Clear inputs left over from the previous profile so two job apps
        // for two different people don't accidentally cross-pollinate.
        TxtJobUrl.Text = "";
        ResultPanel.Visibility = Visibility.Collapsed;
        GhostRiskPanel.Visibility = Visibility.Collapsed;
        ConfirmPanel.Visibility = Visibility.Collapsed;
        StatusText.Text = "";
    }

    // ── APP-02: discovery entrypoint ─────────────────────────────────────

    /// <summary>
    /// A posting arrived from Find Jobs with its description and ghost score
    /// already in hand. Prefills the view, shows the carried score instantly,
    /// and (with exactly one profile, or the already-selected one) starts
    /// tailoring right away — one click from a search result to a filled,
    /// review-ready application.
    /// </summary>
    public void LoadFromDiscovery(Envoy.GhostDetection.Models.JobPosting posting, GhostScoreSnapshot? snapshot)
    {
        // APP-16 audit fix: a running flow owns this view — a second entry
        // (discovery or manual) would stomp state mid-flight.
        if (_flowRunning)
        {
            StatusText.Text = "A flow is already running. Wait for it (or cancel) before starting a new one.";
            StatusText.Foreground = Yellow;
            return;
        }

        SetProfileId(_profileId); // resets whatever the previous flow left behind

        var known = ToKnownPosting(posting);

        if (_profileId != Guid.Empty)
        {
            StartDiscoveryApply(known, snapshot);
            return;
        }

        // No profile selected yet: pick the only one automatically, or tell the
        // user exactly what to do (no silent no-op).
        _ = AutoSelectProfileAndTailorAsync(known, snapshot);
    }

    private async Task AutoSelectProfileAndTailorAsync(KnownPosting posting, GhostScoreSnapshot? snapshot)
    {
        try
        {
            var profiles = await _profiles.GetAllAsync();
            if (profiles.Count == 0)
            {
                StatusText.Text = "No profile yet. Import your resume on the Dashboard, then this job applies in one click.";
                StatusText.Foreground = Yellow;
                return;
            }
            if (profiles.Count > 1)
            {
                StatusText.Text = $"You have {profiles.Count} profiles — pick one on the Dashboard first, then APPLY.";
                StatusText.Foreground = Yellow;
                return;
            }

            StartDiscoveryApply(posting, snapshot, profiles[0].Id);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Something went wrong: {ex.Message}";
            StatusText.Foreground = Red;
        }
    }

    /// <summary>Arms the view with a discovery posting (and optional carried
    /// score) and runs the no-scrape tailor flow. Profile is either already
    /// selected or passed in.</summary>
    private void StartDiscoveryApply(KnownPosting posting, GhostScoreSnapshot? snapshot, Guid? profileId = null)
    {
        if (profileId is { } id)
            SetProfileId(id); // full reset; then re-arm below

        _discoveryPosting = posting;
        TxtJobUrl.Text = posting.JobUrl;
        _jobDescription = posting.DescriptionText;

        // The score travels with the posting: no re-scoring when a snapshot exists.
        if (snapshot is not null && Enum.TryParse<RiskBand>(snapshot.Band, out var band))
        {
            _ghostScore = new GhostScore
            {
                RiskScore = snapshot.RiskScore,
                Band = band,
                TopEvidence = snapshot.TopEvidence ?? Array.Empty<string>()
            };
            ShowGhostSnapshot(_ghostScore);
        }

        _ = TailorForPreparedPostingAsync(posting.JobUrl);
    }

    private void ShowGhostSnapshot(GhostScore score)
    {
        var (badge, label) = score.Band switch
        {
            RiskBand.High => (Red, "HIGH"),
            RiskBand.Elevated => (Yellow, "ELEVATED"),
            _ => (Green, "OK")
        };

        GhostBadge.Background = badge;
        GhostBandText.Text = score.Band == RiskBand.Neutral ? "OK" : $"{label}  ·  {score.RiskScore:0}";
        GhostEvidenceText.Text = score.TopEvidence.Length > 0
            ? string.Join("\n", score.TopEvidence.Select(ev => "- " + ev))
            : "No ghost-risk signals detected on this posting.";
        GhostRiskPanel.Visibility = Visibility.Visible;
    }

    private static KnownPosting ToKnownPosting(Envoy.GhostDetection.Models.JobPosting p) =>
        new(p.Url ?? string.Empty, p.CompanyName ?? string.Empty, p.JobTitle ?? string.Empty, p.DescriptionText ?? string.Empty);

    /// <summary>
    /// The tailor flow for prepared postings: the description is already
    /// known, so the orchestrator runs tailoring + PDF with no scrape.
    /// </summary>
    private async Task TailorForPreparedPostingAsync(string jobUrl)
    {
        if (_tailoringInFlight) return;
        _tailoringInFlight = true;

        BtnInitiate.IsEnabled = false;
        var token = BeginFlow();
        StatusText.Text = "Tailoring your resume for this posting...";
        StatusText.Foreground = Cyan;
        ResultPanel.Visibility = Visibility.Collapsed;

        try
        {
            var prepared = await _orchestrator.PrepareApplicationAsync(_profileId, jobUrl, _discoveryPosting, token);
            _tailored = prepared.Tailored;
            _jobDescription = prepared.JobDescription;
            ShowResult();
            await ScoreGhostRiskAsync(jobUrl, token);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Tailoring cancelled. Nothing was changed.";
            StatusText.Foreground = Yellow;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Something went wrong: {ex.Message}";
            StatusText.Foreground = Red;
        }
        finally
        {
            _tailoringInFlight = false;
            BtnInitiate.IsEnabled = true;
            EndFlow();
        }
    }

    // ── APP-03: cancellable flows ────────────────────────────────────────

    // A running tailoring or fill is cancellable. The token flows through the
    // orchestrator into the LLM completion (linked token) and the CDP commands
    // (waits honor it), so a hung model or stuck page can no longer wedge the
    // view. APP-16 audit fix: the flag latches — a second flow (button handler,
    // discovery tap, queue APPLY NOW) is refused by the entry points instead of
    // silently disposing the first flow's cancellation source.
    private CancellationToken BeginFlow()
    {
        _flowRunning = true;
        _flowCts?.Dispose();
        _flowCts = new CancellationTokenSource();
        BtnCancelFlow.Visibility = Visibility.Visible;
        BtnCancelFlow.IsEnabled = true;
        return _flowCts.Token;
    }

    private void EndFlow()
    {
        BtnCancelFlow.IsEnabled = false;
        BtnCancelFlow.Visibility = Visibility.Collapsed;
        _flowRunning = false;
        _flowCts?.Dispose();
        _flowCts = null;
    }

    private void BtnCancelFlow_Click(object sender, RoutedEventArgs e)
    {
        if (_flowCts is null) return;
        _flowCts.Cancel();
        StatusText.Text = "Cancelling...";
        StatusText.Foreground = Yellow;
    }

    private async void BtnInitiate_Click(object sender, RoutedEventArgs e)
    {
        if (_flowRunning)
        {
            StatusText.Text = "A flow is already running. Wait for it (or cancel) first.";
            StatusText.Foreground = Yellow;
            return;
        }

        var jobUrl = TxtJobUrl.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(jobUrl))
        {
            StatusText.Text = "Paste a job posting link first.";
            StatusText.Foreground = Red;
            return;
        }

        if (_profileId == Guid.Empty)
        {
            StatusText.Text = "Pick a profile on the Dashboard first.";
            StatusText.Foreground = Yellow;
            return;
        }

        if (!Uri.TryCreate(jobUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            StatusText.Text = "That link doesn't look complete. It needs to start with http:// or https://.";
            StatusText.Foreground = Red;
            return;
        }

        // A manual re-tailor of the SAME discovery posting keeps the fast
        // (no-scrape) path; any other URL is a hand-entered posting.
        if (_discoveryPosting != null
            && !string.Equals(_discoveryPosting.JobUrl, jobUrl, StringComparison.OrdinalIgnoreCase))
        {
            _discoveryPosting = null;
        }

        var scraping = _discoveryPosting is null;
        BtnInitiate.IsEnabled = false;
        BtnExecute.IsEnabled = false; // APP-14 audit: no second flow may start mid-prepare

        var token = BeginFlow();
        StatusText.Text = scraping
            ? "Reading the posting and tailoring your resume..."
            : "Tailoring your resume for this posting...";
        StatusText.Foreground = Cyan;
        ResultPanel.Visibility = Visibility.Collapsed;

        try
        {
            var prepared = await _orchestrator.PrepareApplicationAsync(_profileId, jobUrl, _discoveryPosting, token);
            _tailored = prepared.Tailored;
            _jobDescription = prepared.JobDescription;
            ShowResult();
            await ScoreGhostRiskAsync(jobUrl, token);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Tailoring cancelled. Nothing was changed.";
            StatusText.Foreground = Yellow;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Something went wrong: {ex.Message}";
            StatusText.Foreground = Red;
        }
        finally
        {
            BtnInitiate.IsEnabled = true;
            if (_tailored is not null)
                BtnExecute.IsEnabled = true;
            EndFlow();
        }
    }

    // Scores the posting in front of the user (full scoring; network signals OK for a
    // single job) and surfaces the ghost risk band + evidence before they apply.
    private async Task ScoreGhostRiskAsync(string jobUrl, CancellationToken ct = default)
    {
        if (_tailored == null) return;
        try
        {
            var posting = new JobPosting
            {
                Source = JobSource.Other,
                CompanyName = _tailored.Company,
                JobTitle = _tailored.JobTitle,
                Url = jobUrl,
                DescriptionText = _jobDescription
            };

            var score = await _ghostScorer.ScoreAsync(posting, ct);
            // Held so the score in front of the user travels with the submit;
            // the log and the ledger record what the decision was made against.
            _ghostScore = score;
            ShowGhostSnapshot(score);
        }
        catch
        {
            _ghostScore = null;
            GhostRiskPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowResult()
    {
        if (_tailored == null) return;
        ResultPanel.Visibility = Visibility.Visible;

        ScoreText.Text = $"{_tailored.MatchScore}%";
        SafetyText.Text = _tailored.SafetyResult.Passed ? "Cleared" : "Needs your review";
        SafetyText.Foreground = _tailored.SafetyResult.Passed ? Green : Red;

        ViolationsText.Text = _tailored.SafetyResult.Violations.Any()
            ? string.Join("\n", _tailored.SafetyResult.Violations.Select(v => $"- {v.Type}: {v.Description}"))
            : "";

        ChangesText.Text = _tailored.ChangesMade.Any()
            ? "Changes made:\n" + string.Join("\n", _tailored.ChangesMade.Select(c => $"  - {c}"))
            : "";

        BtnExecute.Visibility = Visibility.Visible;
        StatusText.Text = _tailored.SafetyResult.Passed
            ? "Resume tailored and ready."
            : "Tailored, but check the flagged items before you send.";
    }

    private async void BtnExecute_Click(object sender, RoutedEventArgs e)
    {
        if (_tailored == null || _flowRunning) return;

        BtnExecute.IsEnabled = false;
        BtnInitiate.IsEnabled = false;
        var token = BeginFlow();

        var modeLabel = (CmbMode.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Safe";
        var mode = modeLabel switch
        {
            "Stealth" => ExecutionMode.Stealth,
            "Full-Auto" => ExecutionMode.FullAuto,
            _ => ExecutionMode.Safe
        };

        try
        {

            StatusText.Text = "Connecting to your browser...";
            StatusText.Foreground = Cyan;

            var snapshot = _ghostScore == null
                ? null
                : new GhostScoreSnapshot(_ghostScore.RiskScore, _ghostScore.Band.ToString(), _ghostScore.TopEvidence);

            var log = await _orchestrator.SubmitApplicationAsync(
                _tailored.Id, mode, RequestSubmitConfirmationAsync, snapshot,
                onDuplicateDetected: RequestDuplicateOverrideAsync, ct: token);

            StatusText.Text = log.Status switch
            {
                ApplicationStatus.Completed => log.VerifiedSubmission == false
                    ? "Application submitted (delivery could not be verified — check the browser)."
                    : "Application submitted.",
                ApplicationStatus.NeedsUserSubmit => "Auto-fill done. Review the form in the browser, fill the leftover fields, and press the site's Submit button — nothing is sent by Envoy here.",
                ApplicationStatus.DuplicateSkipped => log.ErrorMessage ?? "Already applied — nothing was sent.",
                ApplicationStatus.DeclinedByUser => "You said no. Nothing was sent.",
                ApplicationStatus.SafeModeStopped => "Held for your review. Nothing was sent.",
                ApplicationStatus.RequiresCaptcha => "There's a CAPTCHA. Solve it in the browser, then run this again — your tailored resume is reused (no new LLM run).",
                ApplicationStatus.Failed => $"That didn't work: {log.ErrorMessage}",
                _ => $"Status: {log.Status}"
            };
            // Declining is a decision, not a failure; don't paint it error-red.
            StatusText.Foreground = log.Status switch
            {
                ApplicationStatus.Completed => Green,
                ApplicationStatus.DuplicateSkipped => Yellow,
                ApplicationStatus.DeclinedByUser => Yellow,
                ApplicationStatus.NeedsUserSubmit => Green,
                _ => Red
            };
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Fill cancelled. The half-filled form stays in the browser; rerun when ready.";
            StatusText.Foreground = Yellow;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Something went wrong: {ex.Message}";
            StatusText.Foreground = Red;
        }
        finally
        {
            ConfirmPanel.Visibility = Visibility.Collapsed;
            BtnExecute.IsEnabled = true;
            EndFlow();
        }
    }

    // Presents an inline Confirm/Cancel gate and blocks until the user decides.
    // Returns true only on explicit confirmation; the submit click never fires
    // otherwise. Marshalled through the Dispatcher so it is safe regardless of
    // which thread the orchestrator resumes the callback on.
    private Task<bool> RequestSubmitConfirmationAsync(string message)
    {
        var tcs = new TaskCompletionSource<bool>();
        Dispatcher.Invoke(() =>
        {
            ConfirmPrompt.Text = message;
            ConfirmPanel.Visibility = Visibility.Visible;
            BtnConfirmSubmit.IsEnabled = true;
            BtnCancelSubmit.IsEnabled = true;
            StatusText.Text = "Waiting on you. Confirm or cancel below.";
            StatusText.Foreground = Yellow;
            _confirmTcs = tcs;
        });
        return tcs.Task;
    }

    // The duplicate guard (APP-04) found a completed submission for this
    // posting. Defaulting to NO is deliberate: a re-apply must be a conscious,
    // one-keystroke-away decision, never an accident. Same marshalling rules
    // as the submit gate.
    private Task<bool> RequestDuplicateOverrideAsync(string message)
    {
        var tcs = new TaskCompletionSource<bool>();
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = message;
            StatusText.Foreground = Yellow;
            var choice = MessageBox.Show(
                message + Environment.NewLine + Environment.NewLine +
                "Apply again anyway?",
                "Envoy: already applied here",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            tcs.TrySetResult(choice == MessageBoxResult.Yes);
        });
        return tcs.Task;
    }

    private void BtnConfirmSubmit_Click(object sender, RoutedEventArgs e) => ResolveConfirmation(true);

    private void BtnCancelSubmit_Click(object sender, RoutedEventArgs e) => ResolveConfirmation(false);

    private void ResolveConfirmation(bool approved)
    {
        ConfirmPanel.Visibility = Visibility.Collapsed;
        BtnConfirmSubmit.IsEnabled = false;
        BtnCancelSubmit.IsEnabled = false;
        _confirmTcs?.TrySetResult(approved);
        _confirmTcs = null;
    }
}
