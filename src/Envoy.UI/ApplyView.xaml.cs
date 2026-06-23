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
            StatusText.Text = "⚠ TARGET URL REQUIRED";
            StatusText.Foreground = Red;
            return;
        }

        if (!Uri.TryCreate(jobUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            StatusText.Text = "⚠ TARGET URL MUST BE A FULL http:// OR https:// LINK";
            StatusText.Foreground = Red;
            return;
        }

        BtnInitiate.IsEnabled = false;
        StatusText.Text = "ANALYZING TARGET AND OPTIMIZING PAYLOAD...";
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
            StatusText.Text = $"✕ ERROR: {ex.Message}";
            StatusText.Foreground = Red;
        }
        finally
        {
            BtnInitiate.IsEnabled = true;
        }
    }

    // Scores the posting in front of the user (full scoring — network signals OK for a
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

            var (badge, label) = score.Band switch
            {
                RiskBand.High => (Red, "HIGH"),
                RiskBand.Elevated => (Yellow, "ELEVATED"),
                _ => (Green, "OK")
            };

            GhostBadge.Background = badge;
            GhostBandText.Text = score.Band == RiskBand.Neutral ? "OK" : $"{label}  ·  {score.RiskScore:0}";
            GhostEvidenceText.Text = score.TopEvidence.Length > 0
                ? string.Join("\n", score.TopEvidence.Select(ev => "• " + ev))
                : "No ghost-risk signals detected on this posting.";
            GhostRiskPanel.Visibility = Visibility.Visible;
        }
        catch
        {
            GhostRiskPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowResult()
    {
        if (_tailored == null) return;
        ResultPanel.Visibility = Visibility.Visible;

        ScoreText.Text = $"{_tailored.MatchScore}%";
        SafetyText.Text = _tailored.SafetyResult.Passed ? "✓ CLEARED" : "⚠ SAFE MODE REQUIRED";
        SafetyText.Foreground = _tailored.SafetyResult.Passed ? Green : Red;

        ViolationsText.Text = _tailored.SafetyResult.Violations.Any()
            ? string.Join("\n", _tailored.SafetyResult.Violations.Select(v => $"⚠ {v.Type}: {v.Description}"))
            : "";

        ChangesText.Text = _tailored.ChangesMade.Any()
            ? "MODIFICATIONS:\n" + string.Join("\n", _tailored.ChangesMade.Select(c => $"  ► {c}"))
            : "";

        BtnExecute.Visibility = Visibility.Visible;
        StatusText.Text = _tailored.SafetyResult.Passed ? "✓ PAYLOAD OPTIMIZED" : "⚠ SAFETY PROTOCOLS TRIGGERED";
    }

    private async void BtnExecute_Click(object sender, RoutedEventArgs e)
    {
        if (_tailored == null) return;

        BtnExecute.IsEnabled = false;
        var mode = ((ComboBoxItem)CmbMode.SelectedItem).Content.ToString() == "Stealth"
            ? ExecutionMode.Stealth : ExecutionMode.Safe;

        StatusText.Text = "ESTABLISHING CONNECTION...";
        StatusText.Foreground = Cyan;

        try
        {
            var log = await _orchestrator.SubmitApplicationAsync(
                _tailored.Id, mode, RequestSubmitConfirmationAsync);

            StatusText.Text = log.Status switch
            {
                ApplicationStatus.Completed => "✓ MISSION ACCOMPLISHED",
                ApplicationStatus.SafeModeStopped => "⏸ SUBMISSION HELD — NOT SUBMITTED",
                ApplicationStatus.RequiresCaptcha => "🧩 CAPTCHA DETECTED — HUMAN INPUT NEEDED",
                ApplicationStatus.Failed => $"✕ FAILED: {log.ErrorMessage}",
                _ => $"STATUS: {log.Status}"
            };
            StatusText.Foreground = log.Status == ApplicationStatus.Completed ? Green : Red;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"✕ ERROR: {ex.Message}";
            StatusText.Foreground = Red;
        }
        finally
        {
            ConfirmPanel.Visibility = Visibility.Collapsed;
            BtnExecute.IsEnabled = true;
        }
    }

    // Presents an inline Confirm/Cancel gate and blocks until the user decides.
    // Returns true only on explicit confirmation — the submit click never fires
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
            StatusText.Text = "⏸ AWAITING YOUR CONFIRMATION...";
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