using Envoy.Core.Configuration;
using Envoy.Core.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static Envoy.UI.Theme;

namespace Envoy.UI;

public class BrowserCard
{
    public BrowserType Type { get; set; }
    public string DisplayName { get; set; } = "";
    public string IconGlyph { get; set; } = "\u25C8";
    public string StealthRating { get; set; } = "";
    public string StealthNote { get; set; } = "";
    public string StatusText { get; set; } = "";
    public bool IsInstalled { get; set; }
    public bool IsRunning { get; set; }
    public bool IsSelected { get; set; }
}

public partial class BrowserSelectionView : UserControl
{
    private readonly IBrowserLauncher _browserLauncher;
    private readonly EnvoySettings _settings;
    private List<BrowserCard> _cards = new();

    private static readonly Dictionary<BrowserType, string> BrowserIcons = new()
    {
        { BrowserType.Chrome, "\uE97A" },
        { BrowserType.Edge, "\uE96F" },
        { BrowserType.Brave, "\u2694" },
        { BrowserType.Opera, "\u266B" },
        { BrowserType.Vivaldi, "\u2605" }
    };

    public BrowserSelectionView(IBrowserLauncher browserLauncher, EnvoySettings settings)
    {
        _browserLauncher = browserLauncher;
        _settings = settings;
        InitializeComponent();
        Loaded += BrowserSelectionView_Loaded;
    }

    private async void BrowserSelectionView_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshStealthStatus();
        RefreshScreenshotStatus();
        RefreshFullAutoStatus();
        RefreshDrainWindow();
        try
        {
            await ScanBrowsersAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BrowserSelectionView] Initial scan failed: {ex}");
        }
    }

    private async Task ScanBrowsersAsync()
    {
        BtnScan.IsEnabled = false;

        try
        {
            var browsers = await _browserLauncher.DetectBrowsersAsync();
            _cards = browsers.Select(b => new BrowserCard
            {
                Type = b.Type,
                DisplayName = b.DisplayName,
                IconGlyph = BrowserIcons.TryGetValue(b.Type, out var icon) ? icon : "\u25C8",
                StealthRating = b.StealthRating,
                StealthNote = b.StealthNote,
                StatusText = !b.IsInstalled ? "NOT FOUND" : b.IsRunning ? "DEBUG BRIDGE ACTIVE" : b.IsProcessRunning ? "RUNNING (NO DEBUG)" : "INSTALLED",
                IsInstalled = b.IsInstalled,
                IsRunning = b.IsRunning,
                IsSelected = _browserLauncher.GetSelectedBrowser()?.Type == b.Type
            }).ToList();

            var installed = _cards.Where(c => c.IsInstalled).ToList();

            NoBrowsersLabel.Visibility = installed.Any() ? Visibility.Collapsed : Visibility.Visible;
            BrowserList.ItemsSource = installed;

            if (_browserLauncher.GetSelectedBrowser() == null && installed.Any())
            {
                _browserLauncher.SetSelectedBrowser(installed.First().Type);
                foreach (var card in _cards) card.IsSelected = card.Type == installed.First().Type;
                BrowserList.ItemsSource = null;
                BrowserList.ItemsSource = installed;
            }
        }
        catch (Exception ex)
        {
            NoBrowsersLabel.Text = $"SCAN ERROR: {ex.Message}";
            NoBrowsersLabel.Visibility = Visibility.Visible;
        }
        finally
        {
            BtnScan.IsEnabled = true;
        }
    }

    private void BrowserCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is BrowserCard card)
        {
            _browserLauncher.SetSelectedBrowser(card.Type);
            foreach (var c in _cards) c.IsSelected = c.Type == card.Type;
            var installed = _cards.Where(c => c.IsInstalled).ToList();
            BrowserList.ItemsSource = null;
            BrowserList.ItemsSource = installed;

            var mainWindow = (MainWindow)Window.GetWindow(this);
            mainWindow.UpdateBrowserStatus();
        }
    }

    private void BrowserCard_Enter(object sender, MouseEventArgs e)
    {
        if (sender is Border border && border.DataContext is BrowserCard card && !card.IsSelected)
            border.BorderBrush = CyanHover;
    }

    private void BrowserCard_Leave(object sender, MouseEventArgs e)
    {
        if (sender is Border border && border.DataContext is BrowserCard card && !card.IsSelected)
            border.BorderBrush = BorderColor;
    }

    private async void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ScanBrowsersAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BrowserSelectionView] Rescan failed: {ex}");
        }
    }

    private void RefreshStealthStatus()
    {
        if (_settings.StealthModeEnabled)
        {
            StealthStatusText.Text = "ENABLED";
            StealthStatusText.Foreground = Green;
            BtnToggleStealth.Content = "DISABLE STEALTH INPUT";
        }
        else
        {
            StealthStatusText.Text = "DISABLED";
            StealthStatusText.Foreground = Gray;
            BtnToggleStealth.Content = "ENABLE STEALTH INPUT";
        }
    }

    private void BtnToggleStealth_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.StealthModeEnabled)
        {
            // Guarded enable: deliberate acknowledgement before turning stealth input on.
            var result = MessageBox.Show(
                "Enable stealth input emulation?\n\n" +
                "This makes Envoy type and move the mouse with human-like timing during the form-fill, " +
                "in the human-gated apply flow only. It does NOT solve CAPTCHAs and is NEVER used for " +
                "scraping or job discovery.\n\n" +
                "You are responsible for complying with the terms of use of any site you apply to. " +
                "Enable only if you understand and accept that.",
                "Enable Stealth Input",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;
            _settings.StealthModeEnabled = true;
        }
        else
        {
            _settings.StealthModeEnabled = false;
        }

        if (!_settings.Save())
            MessageBox.Show("Couldn't save the setting. settings.json may be locked, so the change was not stored.",
                "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        RefreshStealthStatus();
    }

    private void RefreshScreenshotStatus()
    {
        if (_settings.CaptureScreenshots)
        {
            ScreenshotStatusText.Text = "ENABLED";
            ScreenshotStatusText.Foreground = Green;
            BtnToggleScreenshots.Content = "DISABLE SCREENSHOTS";
        }
        else
        {
            ScreenshotStatusText.Text = "DISABLED";
            ScreenshotStatusText.Foreground = Gray;
            BtnToggleScreenshots.Content = "ENABLE SCREENSHOTS";
        }
    }

    private void BtnToggleScreenshots_Click(object sender, RoutedEventArgs e)
    {
        _settings.CaptureScreenshots = !_settings.CaptureScreenshots;
        if (!_settings.Save())
            MessageBox.Show("Couldn't save the setting. settings.json may be locked, so the change was not stored.",
                "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        RefreshScreenshotStatus();
    }

    // ── APP-12: Full-Auto guarded toggle ─────────────────────────────────

    private void RefreshFullAutoStatus()
    {
        if (_settings.FullAutoEnabled)
        {
            FullAutoStatusText.Text = "ENABLED (trusted sites only)";
            FullAutoStatusText.Foreground = Green;
            BtnToggleFullAuto.Content = "DISABLE FULL-AUTO";
        }
        else
        {
            FullAutoStatusText.Text = "DISABLED";
            FullAutoStatusText.Foreground = Gray;
            BtnToggleFullAuto.Content = "ENABLE FULL-AUTO";
        }
    }

    private void BtnToggleFullAuto_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.FullAutoEnabled)
        {
            // Guarded enable mirroring the stealth acknowledgement: a deliberate
            // act, with the boundary spelled out BEFORE anything turns on.
            var result = MessageBox.Show(
                "Enable Full-Auto submission?\n\n" +
                "Envoy fills and submits prepared applications WITHOUT asking per site — but ONLY on:\n" +
                " • the company's own ATS boards (classified in code),\n" +
                " • hosts that are trusted: 3 verified submissions, or added to FullAutoTrustedHosts in settings.json.\n\n" +
                "LinkedIn, Indeed, ZipRecruiter, and every aggregator are structurally excluded. " +
                "CAPTCHAs hand off to you; nothing is solved. Unknown domains fail closed.\n\n" +
                "Enable Full-Auto?",
                "Envoy: enable Full-Auto", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

            if (result != MessageBoxResult.Yes) return;
            _settings.FullAutoEnabled = true;
        }
        else
        {
            _settings.FullAutoEnabled = false;
        }

        if (!_settings.Save())
            MessageBox.Show("Couldn't save the setting. settings.json may be locked, so the change was not stored.",
                "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        RefreshFullAutoStatus();
    }

    // ── #92: Night Owl — the Full-Auto drain window ──────────────────────

    private void RefreshDrainWindow()
    {
        TxtDrainWindowStart.Text = _settings.FullAutoWindowStartHour?.ToString() ?? "";
        TxtDrainWindowEnd.Text = _settings.FullAutoWindowEndHour?.ToString() ?? "";

        // No color here: the Loaded path keeps the XAML's gray, and
        // SaveDrainWindowResult colors the save feedback itself.

        DrainWindowStatusText.Text = (_settings.FullAutoWindowStartHour, _settings.FullAutoWindowEndHour) switch
        {
            (null, null) => "No window — prepared rows drain at any hour (the worker polls every few seconds).",
            ({ } s, null) => $"Drains again each hour at or after {s:00} UTC.",
            (null, { } e) => $"Drains until {e:00} UTC; other hours stay dormant.",
            ({ } s, { } e) => $"Drains inside {s:00}–{e:00} UTC (end exclusive, wraps midnight). Rows prepared outside the window wait and drain the moment it opens — no manual nudge."
        };
    }

    private bool SaveDrainWindowResult(string successMessage)
    {
        var saved = _settings.Save();
        RefreshDrainWindow();
        if (saved)
        {
            DrainWindowStatusText.Text = successMessage;
            DrainWindowStatusText.Foreground = Green;
        }
        else
        {
            DrainWindowStatusText.Text = "Applied for this session — settings.json was locked, so the change was not stored.";
            DrainWindowStatusText.Foreground = Yellow;
        }
        return saved;
    }

    private void BtnSaveDrainWindow_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtDrainWindowStart.Text.Trim(), out var start) || start is < 0 or > 23
            || !int.TryParse(TxtDrainWindowEnd.Text.Trim(), out var end) || end is < 0 or > 23)
        {
            DrainWindowStatusText.Text = "Hours must be 0–23 (UTC). Nothing was saved.";
            DrainWindowStatusText.Foreground = Yellow;
            return;
        }

        _settings.FullAutoWindowStartHour = start;
        _settings.FullAutoWindowEndHour = end;
        SaveDrainWindowResult($"Saved — the queue drains inside {start:00}–{end:00} UTC and wakes on it.");
    }

    private void BtnClearDrainWindow_Click(object sender, RoutedEventArgs e)
    {
        _settings.FullAutoWindowStartHour = null;
        _settings.FullAutoWindowEndHour = null;
        SaveDrainWindowResult("Cleared — prepared rows drain at any hour.");
    }
}