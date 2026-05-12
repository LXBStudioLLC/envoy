using Envoy.Core.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Envoy.UI;

public partial class MainWindow : Window
{
    private static readonly SolidColorBrush TitleCyan = new(Color.FromRgb(0x00, 0xF0, 0xFF));
    private static readonly SolidColorBrush TitleMagenta = new(Color.FromRgb(0xFF, 0x00, 0xFF));

    private readonly DashboardView _dashboard;
    private readonly ApplyView _apply;
    private readonly VaultView _vault;
    private readonly BrowserSelectionView _browser;
    private readonly LLMSettingsView _llmSettings;
    private readonly IBrowserLauncher _browserLauncher;
    private DispatcherTimer? _glitchTimer;
    private DispatcherTimer? _statusTimer;
    private readonly TranslateTransform _titleTransform = new(0, 0);
    private Random _rng = new();

    public MainWindow(DashboardView dashboard, ApplyView apply, VaultView vault, BrowserSelectionView browser, LLMSettingsView llmSettings, IBrowserLauncher browserLauncher)
    {
        _dashboard = dashboard;
        _apply = apply;
        _vault = vault;
        _browser = browser;
        _llmSettings = llmSettings;
        _browserLauncher = browserLauncher;

        InitializeComponent();

        TitleCyan.Freeze();
        TitleMagenta.Freeze();
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
                TitleText.Foreground = _rng.NextDouble() < 0.5 ? TitleCyan : TitleMagenta;

                Task.Delay(80).ContinueWith(_ => Dispatcher.Invoke(() =>
                {
                    _titleTransform.X = 0;
                    _titleTransform.Y = 0;
                    TitleText.Foreground = TitleCyan;
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
        UpdateHardwareStatus();
        StartStatusPolling();
    }

    private async void UpdateHardwareStatus()
    {
        try
        {
            var profiler = new HardwareProfiler();
            var hw = profiler.DetectHardware();

            GpuLabel.Text = hw.HasGpu ? $"◉ GPU: {hw.GpuName}" : "◎ GPU: None detected";
            GpuLabel.Foreground = hw.HasGpu
                ? new SolidColorBrush(Color.FromRgb(0x39, 0xFF, 0x14))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0x07, 0x3A));

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
                ChromeLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x39, 0xFF, 0x14));
            }
            else if (browserProcessRunning)
            {
                ChromeLabel.Text = $"⚠ {browserName} (no debug)";
                ChromeLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xE6, 0x00));
            }
            else
            {
                ChromeLabel.Text = "⚠ Browser Offline";
                ChromeLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x07, 0x3A));
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

    public void NavigateTo(UserControl view)
    {
        var oldContent = ContentArea.Children.OfType<UserControl>().FirstOrDefault();
        if (oldContent != null)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120));
            fadeOut.Completed += (_, _) =>
            {
                ContentArea.Children.Clear();
                view.Opacity = 0;
                view.RenderTransform = new TranslateTransform(20, 0);
                ContentArea.Children.Add(view);

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
        var cyan = new SolidColorBrush(Color.FromArgb(40, 0, 240, 255));
        var transparent = new SolidColorBrush(Colors.Transparent);
        var cyanBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xF0, 0xFF));
        var grayBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x92, 0xA4));

        NavDashboard.Background = active == "Dashboard" ? cyan : transparent;
        NavDashboard.Foreground = active == "Dashboard" ? cyanBrush : grayBrush;
        NavApply.Background = active == "Apply" ? cyan : transparent;
        NavApply.Foreground = active == "Apply" ? cyanBrush : grayBrush;
        NavVault.Background = active == "Vault" ? cyan : transparent;
        NavVault.Foreground = active == "Vault" ? cyanBrush : grayBrush;
        NavBrowser.Background = active == "Browser" ? cyan : transparent;
        NavBrowser.Foreground = active == "Browser" ? cyanBrush : grayBrush;
        NavLLM.Background = active == "LLM" ? cyan : transparent;
        NavLLM.Foreground = active == "LLM" ? cyanBrush : grayBrush;
    }

    private void NavDashboard_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(_dashboard);
        UpdateNavButtons("Dashboard");
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