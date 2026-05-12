using Envoy.Core.Models;
using Envoy.Core.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Envoy.UI;

public partial class ApplyView : UserControl
{
    private readonly ApplicationOrchestrator _orchestrator;
    private Guid _profileId;
    private TailoredProfile? _tailored;

    private static readonly SolidColorBrush Cyan = new(Color.FromRgb(0x00, 0xF0, 0xFF));
    private static readonly SolidColorBrush Green = new(Color.FromRgb(0x39, 0xFF, 0x14));
    private static readonly SolidColorBrush Red = new(Color.FromRgb(0xFF, 0x07, 0x3A));
    private static readonly SolidColorBrush Yellow = new(Color.FromRgb(0xFF, 0xE6, 0x00));

    public ApplyView(ApplicationOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        InitializeComponent();
    }

    public void SetProfileId(Guid profileId)
    {
        _profileId = profileId;
        _tailored = null;
        // Clear inputs left over from the previous profile so two job apps
        // for two different people don't accidentally cross-pollinate.
        TxtJobUrl.Text = "";
        ResultPanel.Visibility = Visibility.Collapsed;
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
            _tailored = await _orchestrator.PrepareApplicationAsync(_profileId, jobUrl);
            ShowResult();
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
                _tailored.Id, mode,
                async msg =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = msg;
                        StatusText.Foreground = Yellow;
                    });
                    await Task.CompletedTask;
                });

            StatusText.Text = log.Status switch
            {
                ApplicationStatus.Completed => "✓ MISSION ACCOMPLISHED",
                ApplicationStatus.SafeModeStopped => "⏸ SAFE MODE ENGAGED — REVIEW REQUIRED",
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
            BtnExecute.IsEnabled = true;
        }
    }
}