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
    private readonly DashboardView _dashboard;
    private readonly FindJobsView _find;
    private readonly ApplyView _apply;
    private readonly VaultView _vault;
    private readonly BrowserSelectionView _browser;
    private readonly LLMSettingsView _llmSettings;
    private readonly IBrowserLauncher _browserLauncher;
    private readonly HardwareProfiler _hardwareProfiler;
    private DispatcherTimer? _glitchTimer;
    private DispatcherTimer? _statusTimer;
    private readonly TranslateTransform _titleTransform = new(0, 0);
    private Random _rng = new();

    public MainWindow(DashboardView dashboard, FindJobsView find, ApplyView apply, VaultView vault, BrowserSelectionView browser, LLMSettingsView llmSettings, IBrowserLauncher browserLauncher, HardwareProfiler hardwareProfiler)
    {
        _dashboard = dashboard;
        _find = find;
        _apply = apply;
        _vault = vault;
        _browser = browser;
        _llmSettings = llmSettings;
        _browserLauncher = browserLauncher;
        _hardwareProfiler = hardwareProfiler;

        InitializeComponent();

        TitleText.RenderTransform = _titleTransform;

        Closed += MainWindow_Closed;

        NavigateTo(_dashboard);
        UpdateNavButtons("Dashboard");

        StartGlitchEffect();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _glitchTimer?.Stop();
        _statusTimer?.Stop();
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
        VersionText.Text = $" SOVEREIGN JOB AGENT  v{GetAppVersion()}";
        UpdateHardwareStatus();
        StartStatusPolling();
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
            "Ghost-job detection + a human-gated apply copilot.\n\n" +
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

            GpuLabel.Text = hw.HasGpu ? $"◉ GPU: {hw.GpuName}" : "◎ GPU: None detected";
            GpuLabel.Foreground = hw.HasGpu ? Green : Red;

            ModelLabel.Text = $"◈ {hw.RecommendedModel} ({hw.RecommendedQuantization})";
        }
        catch
        {
            GpuLabel.Text = "◎ GPU: Detection failed";
            ModelLabel.Text = "◈ Default: qwen2.5-coder:14b";
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
                ChromeLabel.Text = $"◉ {browserName} Ready";
                ChromeLabel.Foreground = Green;
            }
            else if (browserProcessRunning)
            {
                ChromeLabel.Text = $"⚠ {browserName} (no debug)";
                ChromeLabel.Foreground = Yellow;
            }
            else
            {
                ChromeLabel.Text = "⚠ Browser Offline";
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
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    public void NavigateToApply(Guid profileId)
    {
        _apply.SetProfileId(profileId);
        NavigateTo(_apply);
        UpdateNavButtons("Apply");
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