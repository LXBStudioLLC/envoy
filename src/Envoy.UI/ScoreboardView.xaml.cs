using Envoy.Core.Configuration;
using Envoy.Core.Models;
using Envoy.Core.Services;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static Envoy.UI.Theme;

namespace Envoy.UI;

public partial class ScoreboardView : UserControl
{
    private readonly IJobEventRepository _jobEvents;
    private readonly EnvoySettings _settings;
    private bool _suppressMinutesChanged;

    public ScoreboardView(IJobEventRepository jobEvents, EnvoySettings settings)
    {
        _jobEvents = jobEvents;
        _settings = settings;
        InitializeComponent();
        SelectMinutesItem(_settings.MinutesPerApplicationEstimate);
        Loaded += async (_, _) => await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            var events = await _jobEvents.GetAllAsync();
            var stats = ScoreboardCalculator.Compute(events, _settings.MinutesPerApplicationEstimate, DateTime.Now);
            Render(stats, events.Count == 0);
        }
        catch (Exception ex)
        {
            StatusLine.Text = $"Couldn't load your stats: {ex.Message}";
            StatusLine.Foreground = Red;
        }
    }

    private void Render(ScoreboardStats stats, bool ledgerEmpty)
    {
        DodgedValue.Text = stats.GhostsDodged.ToString();
        HoursValue.Text = stats.HoursSaved.ToString("0.0");
        HoursFormula.Text = $"{stats.GhostsDodged} dodge{(stats.GhostsDodged == 1 ? "" : "s")} × {stats.MinutesPerApplication} min";
        StreakValue.Text = stats.StreakDays.ToString();
        SurfacedValue.Text = stats.GhostsSurfaced.ToString();

        ContextLine.Text = $"Screened {stats.PostingsScreened} posting{(stats.PostingsScreened == 1 ? "" : "s")}, sent {stats.Applications} application{(stats.Applications == 1 ? "" : "s")}.";
        ColdStartLabel.Visibility = ledgerEmpty ? Visibility.Visible : Visibility.Collapsed;

        var receipts = stats.RecentDodges.Select(ToReceiptItem).ToList();
        ReceiptsList.ItemsSource = receipts;
        NoReceiptsLabel.Visibility = receipts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static ReceiptItem ToReceiptItem(DodgeReceipt receipt)
    {
        var (brush, label) = receipt.RiskBand switch
        {
            "High" => ((Brush)Red, "HIGH"),
            "Elevated" => (Yellow, "ELEVATED"),
            _ => (Gray, receipt.RiskBand.ToUpperInvariant())
        };

        return new ReceiptItem
        {
            Company = string.IsNullOrWhiteSpace(receipt.Company) ? "Unknown company" : receipt.Company,
            Title = string.IsNullOrWhiteSpace(receipt.JobTitle) ? "Untitled posting" : receipt.JobTitle,
            DateText = $"Dodged {receipt.OccurredAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}",
            BadgeText = receipt.RiskScore is { } score ? $"{label} {score:0}" : label,
            BadgeBrush = brush,
            Evidence = string.IsNullOrWhiteSpace(receipt.Evidence)
                ? ""
                : string.Join("\n", receipt.Evidence.Split('\n').Select(line => "- " + line)),
            EvidenceVisibility = string.IsNullOrWhiteSpace(receipt.Evidence) ? Visibility.Collapsed : Visibility.Visible,
            Url = receipt.JobUrl
        };
    }

    private void SelectMinutesItem(int minutes)
    {
        _suppressMinutesChanged = true;
        try
        {
            foreach (var obj in CmbMinutes.Items)
            {
                if (obj is ComboBoxItem item && item.Tag is string tag
                    && int.TryParse(tag, out var value) && value == minutes)
                {
                    CmbMinutes.SelectedItem = item;
                    return;
                }
            }
            // A hand-edited settings value that isn't a preset: leave the box
            // unselected; the formula line still shows the value in effect.
        }
        finally
        {
            _suppressMinutesChanged = false;
        }
    }

    private async void CmbMinutes_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressMinutesChanged) return;
        if (CmbMinutes.SelectedItem is not ComboBoxItem item || item.Tag is not string tag
            || !int.TryParse(tag, out var minutes))
            return;

        _settings.MinutesPerApplicationEstimate = minutes;
        if (!_settings.Save())
        {
            StatusLine.Text = "Couldn't save settings. settings.json may be locked, so this estimate lasts until you close Envoy.";
            StatusLine.Foreground = Yellow;
        }
        else
        {
            StatusLine.Text = "";
        }
        await RefreshAsync();
    }

    private void BtnViewReceipt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string url && !string.IsNullOrWhiteSpace(url))
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                StatusLine.Text = $"Couldn't open the link: {ex.Message}";
                StatusLine.Foreground = Red;
            }
        }
    }
}

public class ReceiptItem
{
    public string Company { get; init; } = "";
    public string Title { get; init; } = "";
    public string DateText { get; init; } = "";
    public string BadgeText { get; init; } = "";
    public Brush BadgeBrush { get; init; } = Brushes.Gray;
    public string Evidence { get; init; } = "";
    public Visibility EvidenceVisibility { get; init; } = Visibility.Collapsed;
    public string Url { get; init; } = "";
}
