using Envoy.Core.Configuration;
using Envoy.Core.Models;
using Envoy.Core.Services;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using static Envoy.UI.Theme;

namespace Envoy.UI;

public partial class MainWindow : Window
{
    private readonly ScoreboardView _scoreboard;
    private readonly DashboardView _dashboard;
    private readonly FindJobsView _find;
    private readonly ApplyView _apply;
    private readonly VaultView _vault;
    private readonly BrowserSelectionView _browser;
    private readonly LLMSettingsView _llmSettings;
    private readonly IBrowserLauncher _browserLauncher;
    private readonly HardwareProfiler _hardwareProfiler;
    private readonly IUpdateCheckService _updateCheck;
    private readonly EnvoySettings _settings;
    private readonly ApplicationQueueService _queue;
    private readonly IApplicationQueueRepository _queueRepo;
    private TrayPresence? _tray;
    private bool _exiting;
    private bool _minimizedToastShown;
    private string _queueTooltip = "Envoy";
    private int _lastActiveQueueCount;
    private bool _lastActiveQueueCountKnown;
    private readonly Dictionary<Guid, QueueItemStatus> _lastQueueStatus = new();
    private readonly object _queueStatusLock = new();
    private string? _updateUrl;
    private DispatcherTimer? _glitchTimer;
    private DispatcherTimer? _statusTimer;
    private readonly TranslateTransform _titleTransform = new(0, 0);
    private Random _rng = new();

    public MainWindow(ScoreboardView scoreboard, DashboardView dashboard, FindJobsView find, ApplyView apply, VaultView vault, BrowserSelectionView browser, LLMSettingsView llmSettings, IBrowserLauncher browserLauncher, HardwareProfiler hardwareProfiler, IUpdateCheckService updateCheck, EnvoySettings settings, ApplicationQueueService queue, IApplicationQueueRepository queueRepo)
    {
        _scoreboard = scoreboard;
        _dashboard = dashboard;
        _find = find;
        _apply = apply;
        _vault = vault;
        _browser = browser;
        _llmSettings = llmSettings;
        _browserLauncher = browserLauncher;
        _hardwareProfiler = hardwareProfiler;
        _updateCheck = updateCheck;
        _settings = settings;
        _queue = queue;
        _queueRepo = queueRepo;

        // APP-02: route the Find Jobs APPLY action straight into the Apply
        // view, carrying the posting + ghost snapshot so nothing re-scrapes.
        find.ApplyRequested += item => NavigateToApplyFromDiscovery(item, find.ResolveApplyProfileId());

        // APP-07: route ready-to-fill queue items into a pre-tailored session.
        find.PreparedApplyRequested += item => NavigateToApplyPrepared(item);

        InitializeComponent();

        TitleText.RenderTransform = _titleTransform;

        Closed += MainWindow_Closed;
        Closing += MainWindow_Closing;

        // ── #92: tray presence + queue notifications. The tray observes the
        // queue, never drives it: toasts are advisory, the worker owns its loop.
        _queue.QueueChanged += () => QueueChangedOnTrayAsync();
        _tray = new TrayPresence(
            tooltipProvider: () => _queueTooltip,
            restoreWindow: () => { Show(); WindowState = WindowState.Normal; Activate(); },
            exitApplication: () => { _exiting = true; Application.Current.Shutdown(); });

        // The scoreboard observes the same tray for refresh-finished toasts.
        _scoreboard.SetTray(_tray);
        // The scoreboard is the front door: opening Envoy should feel like
        // checking the score, not starting a chore.
        NavigateTo(_scoreboard);
        UpdateNavButtons("Scoreboard");

        StartGlitchEffect();
    }

    // ── #92: tray + queue notifications ──────────────────────────────────

    /// <summary>Cached one-line queue state for the tray tooltip. Refreshed on
    /// every QueueChanged; the provider closure never touches the database on
    /// the tooltip thread.</summary>
    private async void QueueChangedOnTrayAsync()
    {
        List<QueuedApplication> rows;
        try { rows = await _queueRepo.GetAllAsync(); }
        catch { return; }

        var toasts = new List<(string Title, string Body, bool Warning)>();
        var sentThisCycle = 0;
        var failedThisCycle = 0;

        lock (_queueStatusLock)
        {
            foreach (var row in rows)
            {
                var previous = _lastQueueStatus.TryGetValue(row.Id, out var prev)
                    ? prev
                    : QueueItemStatus.Queued;
                if (row.Status != previous)
                {
                    switch (row.Status)
                    {
                        case QueueItemStatus.AppReady:
                            toasts.Add(("READY TO FILL",
                                $"{row.Company}: tailoring finished — one click in FIND JOBS finishes the review.", false));
                            break;
                        case QueueItemStatus.NeedsCaptcha:
                            toasts.Add(("CAPTCHA NEEDS YOU",
                                $"{row.Company}: solve it in the browser, then requeue. Envoy never solves them.", true));
                            break;
                        case QueueItemStatus.NeedsUserSend:
                            toasts.Add(("PRESS THE SITE'S SUBMIT",
                                $"{row.Company}: the form is filled — the send button stays yours.", false));
                            break;
                        case QueueItemStatus.Failed:
                            toasts.Add(("QUEUE ITEM FAILED",
                                $"{row.Company}: {row.Note}", true));
                            failedThisCycle++;
                            break;
                        case QueueItemStatus.Submitted:
                            sentThisCycle++;
                            break;
                    }
                }
                _lastQueueStatus[row.Id] = row.Status;
            }

            // Rows removed from the queue (expired / user removed) fall out of state.
            var liveIds = rows.Select(r => r.Id).ToHashSet();
            var stale = _lastQueueStatus.Keys.Where(k => !liveIds.Contains(k)).ToList();
            foreach (var id in stale)
                _lastQueueStatus.Remove(id);

            var queued = rows.Count(r => r.Status == QueueItemStatus.Queued);
            var ready = rows.Count(r => r.Status == QueueItemStatus.AppReady);
            var sentToday = rows.Count(r => r.Status == QueueItemStatus.Submitted
                && (r.UpdatedAt ?? r.CreatedAt).Date == DateTime.UtcNow.Date);
            var activeCount = rows.Count(r =>
                r.Status is QueueItemStatus.Queued or QueueItemStatus.Preparing or QueueItemStatus.AppReady);

            // The drain is done when nothing is active anymore: say so, once,
            // with the honest counts of what the cycle produced.
            if (_lastActiveQueueCountKnown && _lastActiveQueueCount > 0 && activeCount == 0
                && sentThisCycle + failedThisCycle > 0)
            {
                toasts.Add(("QUEUE FINISHED",
                    $"Drain complete: {sentThisCycle} sent, {failedThisCycle} failed. The scoreboard has the verified detail.", false));
            }

            _queueTooltip = activeCount > 0
                ? $"Envoy — queue: {queued} waiting · {ready} ready · {sentToday} sent today"
                : sentToday > 0
                    ? $"Envoy — idle · {sentToday} sent today"
                    : "Envoy";
            _lastActiveQueueCount = activeCount;
            _lastActiveQueueCountKnown = true;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            _tray?.RefreshTooltip();
            foreach (var (title, body, warning) in toasts)
                _tray?.Toast(title, body, warning);
        });
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exiting) return; // EXIT from the tray: let the window close for real

        // #92: closing minimizes to the tray so a background queue keeps
        // draining with notifications intact. EXIT is the tray's own menu item.
        e.Cancel = true;
        Hide();
        if (!_minimizedToastShown)
        {
            _minimizedToastShown = true;
            _tray?.Toast("ENVOY IS IN THE TRAY", "The queue keeps draining. Right-click the tray icon for EXIT.", false);
        }
    }

    public void NavigateToLLM()
    {
        NavigateTo(_llmSettings);
        UpdateNavButtons("LLM");
    }

    public void NavigateToFindJobs()
    {
        NavigateTo(_find);
        UpdateNavButtons("Find");
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _glitchTimer?.Stop();
        _statusTimer?.Stop();
        _tray?.Dispose(); // never leave a ghost icon in the tray after shutdown
        _tray = null;
    }

    private void StartGlitchEffect()
    {
        _glitchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3500) };
        _glitchTimer.Tick += (_, _) =>
        {
            if (_rng.NextDouble() < 0.3)
            {
                _titleTransform.X = _rng.Next(-3, 4);
                _titleTransform.Y = _rng.Next(-1, 2);
                TitleText.Foreground = _rng.NextDouble() < 0.5 ? Cyan : Magenta;

                Task.Delay(80).ContinueWith(_ => Dispatcher.Invoke(() =>
                {
                    _titleTransform.X = 0;
                    _titleTransform.Y = 0;
                    TitleText.Foreground = Cyan;
                }));
            }
        };
        _glitchTimer.Start();
    }

    private void StartStatusPolling()
    {
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _statusTimer.Tick += async (_, _) =>
        {
            try
            {
                await UpdateBrowserStatusAsync();
            }
            catch { }
        };
        _statusTimer.Start();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        VersionText.Text = $" v{GetAppVersion()}";
        UpdateHardwareStatus();
        StartStatusPolling();
        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        if (!_settings.CheckForUpdates)
            return;

        try
        {
            var update = await _updateCheck.CheckForUpdateAsync(GetAppVersion());
            if (update is null)
                return;

            _updateUrl = update.ReleaseUrl;
            BtnUpdate.Content = $"UPDATE v{update.LatestVersion}";
            BtnUpdate.ToolTip = $"Envoy v{update.LatestVersion} is available. Click to open the release page.";
            BtnUpdate.Visibility = Visibility.Visible;
        }
        catch { /* never disturb startup over an update check */ }
    }

    private void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_updateUrl is null)
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _updateUrl,
                UseShellExecute = true
            });
        }
        catch { /* best-effort; the user can open the releases page manually */ }
    }

    private static string GetAppVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }
        return asm.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    private void BtnAbout_Click(object sender, RoutedEventArgs e)
    {
        var choice = MessageBox.Show(
            $"Envoy v{GetAppVersion()}\nLXB Studio LLC\n\n" +
            "Ghost-job detection and an apply copilot that never sends without you.\n\n" +
            "Found a bug, or a real job flagged as a possible ghost? " +
            "Those reports are especially valuable.\n\n" +
            "Your data and logs stay on this PC in %LOCALAPPDATA%\\Envoy\n\n" +
            "Open the issue tracker to file a report?",
            "About Envoy",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (choice == MessageBoxResult.Yes)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/LXBStudioLLC/envoy/issues/new/choose",
                    UseShellExecute = true
                });
            }
            catch { /* best-effort; the user can navigate manually */ }
        }
    }

    private async void UpdateHardwareStatus()
    {
        try
        {
            var hw = _hardwareProfiler.DetectHardware();

            GpuLabel.Text = hw.HasGpu ? $"GPU: {hw.GpuName}" : "GPU: none detected";
            GpuLabel.Foreground = hw.HasGpu ? Green : Red;

            ModelLabel.Text = $"Model: {hw.RecommendedModel} ({hw.RecommendedQuantization})";
        }
        catch
        {
            GpuLabel.Text = "GPU: check failed";
            ModelLabel.Text = "Model: qwen2.5-coder:14b (default)";
        }

        try
        {
            await UpdateBrowserStatusAsync();
        }
        catch { }
    }

    private async Task UpdateBrowserStatusAsync()
    {
        try
        {
            var browserType = _browserLauncher.GetSelectedBrowserType() ?? BrowserType.Chrome;
            var browserName = _browserLauncher.GetSelectedBrowser()?.DisplayName ?? browserType.ToString();
            var browserReady = await _browserLauncher.IsRunningWithDebuggingAsync(browserType);
            var browserProcessRunning = await _browserLauncher.IsProcessRunningAsync(browserType);

            if (browserReady)
            {
                ChromeLabel.Text = $"{browserName} ready";
                ChromeLabel.Foreground = Green;
            }
            else if (browserProcessRunning)
            {
                ChromeLabel.Text = $"{browserName}: debug bridge off";
                ChromeLabel.Foreground = Yellow;
            }
            else
            {
                ChromeLabel.Text = "Browser offline";
                ChromeLabel.Foreground = Red;
            }
        }
        catch { }
    }

    public async void UpdateBrowserStatus()
    {
        try
        {
            await UpdateBrowserStatusAsync();
        }
        catch { }
    }

    private bool _isTransitioning;

    public void NavigateTo(UserControl view)
    {
        // Drop rapid nav clicks during the fade-out so overlapping animations can't
        // leave a view half-faded.
        if (_isTransitioning) return;

        var oldContent = ContentArea.Children.OfType<UserControl>().FirstOrDefault();
        if (oldContent != null)
        {
            _isTransitioning = true;
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120));
            fadeOut.Completed += (_, _) =>
            {
                ContentArea.Children.Clear();
                view.Opacity = 0;
                view.RenderTransform = new TranslateTransform(20, 0);
                ContentArea.Children.Add(view);
                _isTransitioning = false;

                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                var slideIn = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(200));
                slideIn.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };

                view.BeginAnimation(FrameworkElement.OpacityProperty, fadeIn);
                ((TranslateTransform)view.RenderTransform).BeginAnimation(TranslateTransform.XProperty, slideIn);
            };
            oldContent.BeginAnimation(FrameworkElement.OpacityProperty, fadeOut);
        }
        else
        {
            view.RenderTransform = new TranslateTransform(0, 0);
            ContentArea.Children.Add(view);
        }
    }

    private void UpdateNavButtons(string active)
    {
        NavScoreboard.Background = active == "Scoreboard" ? NavActiveBg : Transparent;
        NavScoreboard.Foreground = active == "Scoreboard" ? Cyan : Gray;
        NavDashboard.Background = active == "Dashboard" ? NavActiveBg : Transparent;
        NavDashboard.Foreground = active == "Dashboard" ? Cyan : Gray;
        NavFind.Background = active == "Find" ? NavActiveBg : Transparent;
        NavFind.Foreground = active == "Find" ? Cyan : Gray;
        NavApply.Background = active == "Apply" ? NavActiveBg : Transparent;
        NavApply.Foreground = active == "Apply" ? Cyan : Gray;
        NavVault.Background = active == "Vault" ? NavActiveBg : Transparent;
        NavVault.Foreground = active == "Vault" ? Cyan : Gray;
        NavBrowser.Background = active == "Browser" ? NavActiveBg : Transparent;
        NavBrowser.Foreground = active == "Browser" ? Cyan : Gray;
        NavLLM.Background = active == "LLM" ? NavActiveBg : Transparent;
        NavLLM.Foreground = active == "LLM" ? Cyan : Gray;
    }

    private void NavScoreboard_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(_scoreboard);
        UpdateNavButtons("Scoreboard");
    }

    private void NavDashboard_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(_dashboard);
        UpdateNavButtons("Dashboard");
    }

    private void NavFind_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(_find);
        UpdateNavButtons("Find");
    }

    private void NavApply_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(_apply);
        UpdateNavButtons("Apply");
    }

    private void NavVault_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(_vault);
        UpdateNavButtons("Vault");
    }

    private void NavBrowser_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(_browser);
        UpdateNavButtons("Browser");
    }

    private void NavLLM_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(_llmSettings);
        UpdateNavButtons("LLM");
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close(); // #92: minimize to tray (Closing decides)

    public void NavigateToApply(Guid profileId)
    {
        _apply.SetProfileId(profileId);
        NavigateTo(_apply);
        UpdateNavButtons("Apply");
    }

    /// <summary>APP-02: one click from a Find Jobs result to a prepared
    /// application. The carried posting + ghost snapshot travel intact; the
    /// Find Jobs APPLY WITH choice (multi-profile) arms the Apply view: a
    /// Guid.Empty means no profile is picked — the Apply view then does its
    /// own selection/auto-select.</summary>
    public void NavigateToApplyFromDiscovery(DiscoveredJobItem item, Guid applyProfileId = default)
    {
        if (applyProfileId != Guid.Empty)
            _apply.SetProfileId(applyProfileId);

        NavigateTo(_apply);
        UpdateNavButtons("Apply");
        _apply.LoadFromDiscovery(item.Posting!, item.Snapshot);
    }

    /// <summary>APP-07: open an AppReady queue item review-ready from the Find
    /// Jobs queue panel.</summary>
    public void NavigateToApplyPrepared(QueuedApplication item)
    {
        NavigateTo(_apply);
        UpdateNavButtons("Apply");

        var snapshot = item.GhostRiskScore is null
            ? null
            : new GhostScoreSnapshot(item.GhostRiskScore.Value, item.GhostRiskBand ?? "Neutral", Array.Empty<string>());
        _apply.LoadPreparedApplication(item.TailoredProfileId!.Value, snapshot);
    }

    public async Task NavigateToVault(Guid profileId)
    {
        NavigateTo(_vault);
        UpdateNavButtons("Vault");
        await _vault.SetProfileId(profileId);
    }

    public void NavigateToDashboard()
    {
        NavigateTo(_dashboard);
        UpdateNavButtons("Dashboard");
    }
}