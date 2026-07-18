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
    private Guid _profileId;
    private TailoredProfile? _tailored;
    private string _jobDescription = "";
    private GhostScore? _ghostScore;
    private TaskCompletionSource<bool>? _confirmTcs;

    public ApplyView(ApplicationOrchestrator orchestrator, GhostScorer ghostScorer, EnvoySettings settings)
    {
        _orchestrator = orchestrator;
        _ghostScorer = ghostScorer;
        _settings = settings;
        InitializeComponent();
        Loaded += (_, _) => RefreshModeOptions();
    }

    // Only offers the Stealth execution mode when stealth input is enabled (the guarded
    // opt-in from the Browser view); otherwise Safe is the only option.
    private void RefreshModeOptions()
    {
        CmbMode.Items.Clear();
        CmbMode.Items.Add(new ComboBoxItem { Content = "Safe", IsSelected = true });
        if (_settings.StealthModeEnabled)
            CmbMode.Items.Add(new ComboBoxItem { Content = "Stealth" });
    }

    public void SetProfileId(Guid profileId)
    {
        _profileId = profileId;
        _tailored = null;
        _jobDescription = "";
        _ghostScore = null;
        // Clear inputs left over from the previous profile so two job apps
        // for two different people don't accidentally cross-pollinate.
        TxtJobUrl.Text = "";
        ResultPanel.Visibility = Visibility.Collapsed;
        GhostRiskPanel.Visibility = Visibility.Collapsed;
        ConfirmPanel.Visibility = Visibility.Collapsed;
        StatusText.Text = "";
    }

    private async void BtnInitiate_Click(object sender, RoutedEventArgs e)
    {
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

        BtnInitiate.IsEnabled = false;
        StatusText.Text = "Reading the posting and tailoring your resume...";
        StatusText.Foreground = Cyan;
        ResultPanel.Visibility = Visibility.Collapsed;

        try
        {
            var prepared = await _orchestrator.PrepareApplicationAsync(_profileId, jobUrl);
            _tailored = prepared.Tailored;
            _jobDescription = prepared.JobDescription;
            ShowResult();
            await ScoreGhostRiskAsync(jobUrl);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Something went wrong: {ex.Message}";
            StatusText.Foreground = Red;
        }
        finally
        {
            BtnInitiate.IsEnabled = true;
        }
    }

    // Scores the posting in front of the user (full scoring; network signals OK for a
    // single job) and surfaces the ghost risk band + evidence before they apply.
    private async Task ScoreGhostRiskAsync(string jobUrl)
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

            var score = await _ghostScorer.ScoreAsync(posting);
            // Held so the score in front of the user travels with the submit;
            // the log and the ledger record what the decision was made against.
            _ghostScore = score;

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
        if (_tailored == null) return;

        BtnExecute.IsEnabled = false;

        try
        {
            var mode = (CmbMode.SelectedItem as ComboBoxItem)?.Content?.ToString() == "Stealth"
                ? ExecutionMode.Stealth : ExecutionMode.Safe;

            StatusText.Text = "Connecting to your browser...";
            StatusText.Foreground = Cyan;

            var snapshot = _ghostScore == null
                ? null
                : new GhostScoreSnapshot(_ghostScore.RiskScore, _ghostScore.Band.ToString(), _ghostScore.TopEvidence);

            var log = await _orchestrator.SubmitApplicationAsync(
                _tailored.Id, mode, RequestSubmitConfirmationAsync, snapshot);

            StatusText.Text = log.Status switch
            {
                ApplicationStatus.Completed => "Application submitted.",
                ApplicationStatus.DeclinedByUser => "You said no. Nothing was sent.",
                ApplicationStatus.SafeModeStopped => "Held for your review. Nothing was sent.",
                ApplicationStatus.RequiresCaptcha => "There's a CAPTCHA. Solve it in the browser, then run this again.",
                ApplicationStatus.Failed => $"That didn't work: {log.ErrorMessage}",
                _ => $"Status: {log.Status}"
            };
            // Declining is a decision, not a failure; don't paint it error-red.
            StatusText.Foreground = log.Status switch
            {
                ApplicationStatus.Completed => Green,
                ApplicationStatus.DeclinedByUser => Yellow,
                _ => Red
            };
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