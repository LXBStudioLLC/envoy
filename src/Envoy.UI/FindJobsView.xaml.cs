using Envoy.Core.Configuration;
using Envoy.Discovery;
using Envoy.Discovery.Models;
using Envoy.GhostDetection;
using Envoy.GhostDetection.Models;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static Envoy.UI.Theme;

namespace Envoy.UI;

public partial class FindJobsView : UserControl
{
    private readonly JobDiscoveryService _discovery;
    private readonly GhostScorer _scorer;
    private readonly EnvoySettings _settings;

    public FindJobsView(JobDiscoveryService discovery, GhostScorer scorer, EnvoySettings settings)
    {
        _discovery = discovery;
        _scorer = scorer;
        _settings = settings;
        InitializeComponent();
        Loaded += FindJobsView_Loaded;
    }

    private void FindJobsView_Loaded(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(TxtBraveKey.Text) && !string.IsNullOrEmpty(_settings.BraveSearchApiKey))
            TxtBraveKey.Text = _settings.BraveSearchApiKey;
    }

    private DiscoveryQuery BuildQuery() => new()
    {
        Keywords = TxtKeywords.Text?.Trim(),
        Location = TxtLocation.Text?.Trim(),
        RemoteOnly = ChkRemote.IsChecked == true,
        MaxResults = 100
    };

    private async void BtnScanBoards_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "SCANNING PUBLIC BOARDS...");
        try
        {
            var result = await _discovery.SearchBoardsAsync(_discovery.DefaultBoards, BuildQuery());
            await RenderAsync(result);
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetBusy(false); }
    }

    private async void BtnWebSearch_Click(object sender, RoutedEventArgs e)
    {
        var key = TxtBraveKey.Text?.Trim() ?? "";
        SaveKeyIfChanged(key);
        SetBusy(true, "SEARCHING THE WEB...");
        try
        {
            var result = await _discovery.WebSearchAsync(key, TxtKeywords.Text?.Trim() ?? "", BuildQuery());
            await RenderAsync(result);
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetBusy(false); }
    }

    private void BtnSaveKey_Click(object sender, RoutedEventArgs e)
    {
        SaveKeyIfChanged(TxtBraveKey.Text?.Trim() ?? "");
        StatusText.Text = "✓ BRAVE KEY SAVED";
        StatusText.Foreground = Green;
    }

    private void SaveKeyIfChanged(string key)
    {
        if (!string.IsNullOrEmpty(key) && key != _settings.BraveSearchApiKey)
        {
            _settings.BraveSearchApiKey = key;
            _settings.Save();
        }
    }

    private async Task RenderAsync(DiscoveryResult result)
    {
        var items = new List<DiscoveredJobItem>(result.Jobs.Count);
        foreach (var job in result.Jobs)
        {
            // Local-only scoring: skips network-bound signals so a 100-row list doesn't
            // fan out one outbound request per posting.
            var score = await _scorer.ScoreAsync(job, localOnly: true);
            items.Add(ToItem(job, score));
        }
        ResultsList.ItemsSource = items;

        if (items.Count == 0)
        {
            var msg = result.Errors.Count > 0 ? string.Join("  |  ", result.Errors) : "No matching jobs found.";
            StatusText.Text = $"⚠ {msg}";
            StatusText.Foreground = result.Errors.Count > 0 ? Yellow : Gray;
        }
        else
        {
            var suffix = result.Errors.Count > 0 ? $"  ·  {result.Errors.Count} source(s) unavailable" : "";
            StatusText.Text = $"✓ {items.Count} JOB(S) FOUND{suffix}";
            StatusText.Foreground = Green;
        }
    }

    private static DiscoveredJobItem ToItem(JobPosting job, GhostScore score)
    {
        var (brush, label) = score.Band switch
        {
            RiskBand.High => ((Brush)Red, "HIGH"),
            RiskBand.Elevated => (Yellow, "ELEVATED"),
            _ => (Green, "OK")
        };

        var evidence = score.TopEvidence.Length > 0
            ? string.Join("\n", score.TopEvidence.Select(ev => "• " + ev))
            : "";

        var meta = $"{job.Source} · {(job.PostedAtUtc?.ToString("yyyy-MM-dd") ?? "date n/a")}";
        if (!string.IsNullOrWhiteSpace(job.SalaryText))
            meta += $" · {job.SalaryText}";

        return new DiscoveredJobItem
        {
            Title = string.IsNullOrWhiteSpace(job.JobTitle) ? "—" : job.JobTitle,
            Company = string.IsNullOrWhiteSpace(job.CompanyName) ? "—" : job.CompanyName,
            Location = string.IsNullOrWhiteSpace(job.Location) ? "—" : job.Location,
            Meta = meta,
            RiskText = score.Band == RiskBand.Neutral ? "OK" : $"{label} {score.RiskScore:0}",
            RiskBrush = brush,
            Evidence = evidence,
            EvidenceVisibility = string.IsNullOrEmpty(evidence) ? Visibility.Collapsed : Visibility.Visible,
            Url = job.Url
        };
    }

    private void BtnView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string url && !string.IsNullOrWhiteSpace(url))
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { ShowError($"Could not open link: {ex.Message}"); }
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        BtnScanBoards.IsEnabled = !busy;
        BtnWebSearch.IsEnabled = !busy;
        if (message != null)
        {
            StatusText.Text = message;
            StatusText.Foreground = Cyan;
        }
    }

    private void ShowError(string message)
    {
        StatusText.Text = $"✕ {message}";
        StatusText.Foreground = Red;
    }
}

public class DiscoveredJobItem
{
    public string Title { get; init; } = "";
    public string Company { get; init; } = "";
    public string Location { get; init; } = "";
    public string Meta { get; init; } = "";
    public string RiskText { get; init; } = "";
    public Brush RiskBrush { get; init; } = Brushes.Gray;
    public string Evidence { get; init; } = "";
    public Visibility EvidenceVisibility { get; init; } = Visibility.Collapsed;
    public string Url { get; init; } = "";
}
