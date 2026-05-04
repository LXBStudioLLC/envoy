using Envoy.Core.Models;
using Envoy.Core.Services;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Envoy.UI;

public partial class DashboardView : UserControl
{
    private readonly ApplicationOrchestrator _orchestrator;
    private readonly IProfileRepository _profileRepo;
    private readonly IBrowserLauncher _browserLauncher;
    private List<MasterProfile> _profiles = new();

    private static readonly SolidColorBrush Cyan = new(Color.FromRgb(0x00, 0xF0, 0xFF));
    private static readonly SolidColorBrush Green = new(Color.FromRgb(0x39, 0xFF, 0x14));
    private static readonly SolidColorBrush Red = new(Color.FromRgb(0xFF, 0x07, 0x3A));
    private static readonly SolidColorBrush Yellow = new(Color.FromRgb(0xFF, 0xE6, 0x00));
    private static readonly SolidColorBrush Gray = new(Color.FromRgb(0x88, 0x92, 0xA4));

    public DashboardView(ApplicationOrchestrator orchestrator, IProfileRepository profileRepo, IBrowserLauncher browserLauncher)
    {
        _orchestrator = orchestrator;
        _profileRepo = profileRepo;
        _browserLauncher = browserLauncher;
        InitializeComponent();
        Loaded += DashboardView_Loaded;
    }

    private async void DashboardView_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadProfilesAsync();
        await AutoLaunchChromeAsync();
    }

    private async Task LoadProfilesAsync()
    {
        try
        {
            _profiles = await _profileRepo.GetAllAsync();
            ProfilesList.ItemsSource = _profiles;
            EmptyProfilesLabel.Visibility = _profiles.Any() ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex)
        {
            ImportStatus.Text = $"✕ ERROR: {ex.Message}";
            ImportStatus.Foreground = Red;
        }
    }

    private async Task AutoLaunchChromeAsync()
    {
        try
        {
var ready = await _browserLauncher.IsRunningWithDebuggingAsync(_browserLauncher.GetSelectedBrowser()?.Type ?? BrowserType.Chrome);
            if (ready)
            {
                ChromeStatusLabel.Text = "◉ Chrome Ready — Debug bridge active";
                ChromeStatusLabel.Foreground = Green;
                ChromeDetailLabel.Text = "Chrome is running with remote debugging on port 9222.";
                ChromeDetailLabel.Foreground = Gray;
                BtnLaunchChrome.Content = "◈ RE-LAUNCH CHROME";
                return;
            }

            ChromeStatusLabel.Text = "◉ Launching Chrome with debug bridge...";
            ChromeStatusLabel.Foreground = Cyan;
            ChromeDetailLabel.Text = "Envoy needs Chrome with remote debugging for stealth automation.";
            ChromeDetailLabel.Foreground = Gray;

            var launched = await _browserLauncher.LaunchAsync(_browserLauncher.GetSelectedBrowser()?.Type ?? BrowserType.Chrome);
            if (launched)
            {
                ChromeStatusLabel.Text = "◉ Chrome Ready — Debug bridge active";
                ChromeStatusLabel.Foreground = Green;
                ChromeDetailLabel.Text = "Chrome launched successfully. You can now apply to job.";
                ChromeDetailLabel.Foreground = Gray;
                BtnLaunchChrome.Content = "◈ RE-LAUNCH CHROME";
            }
            else
            {
                ChromeStatusLabel.Text = "⚠ Chrome not detected";
                ChromeStatusLabel.Foreground = Yellow;
                ChromeDetailLabel.Text = "Install Google Chrome or Microsoft Edge, then click below to retry.";
                ChromeDetailLabel.Foreground = Gray;
                BtnLaunchChrome.Content = "◈ LAUNCH CHROME";
            }
        }
        catch (Exception ex)
        {
            ChromeStatusLabel.Text = $"✕ Chrome error: {ex.Message}";
            ChromeStatusLabel.Foreground = Red;
            ChromeDetailLabel.Text = "Click below to retry launching Chrome.";
            ChromeDetailLabel.Foreground = Gray;
            BtnLaunchChrome.Content = "◈ LAUNCH CHROME";
        }
    }

    private async void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf", Title = "Select Resume PDF" };
        if (dlg.ShowDialog() != true) return;

        BtnImport.IsEnabled = false;
        ImportStatus.Text = "INITIALIZING PARSE SEQUENCE...";
        ImportStatus.Foreground = Cyan;

        try
        {
            var profile = await _orchestrator.ImportResumeAsync(dlg.FileName);
            await LoadProfilesAsync();
            ImportStatus.Text = $"✓ PROFILE IMPORTED: {profile.Name}";
            ImportStatus.Foreground = Green;
        }
        catch (Exception ex)
        {
            ImportStatus.Text = $"✕ ERROR: {ex.Message}";
            ImportStatus.Foreground = Red;
        }
        finally
        {
            BtnImport.IsEnabled = true;
        }
    }

    private async void BtnLaunchChrome_Click(object sender, RoutedEventArgs e)
    {
        BtnLaunchChrome.IsEnabled = false;
        var browserType = _browserLauncher.GetSelectedBrowser()?.Type ?? BrowserType.Chrome;
        var browserName = _browserLauncher.GetSelectedBrowser()?.DisplayName ?? "Browser";
        var ready = await _browserLauncher.IsRunningWithDebuggingAsync(browserType);

        if (ready)
        {
            ChromeStatusLabel.Text = $"◉ {browserName} Already Running";
            ChromeStatusLabel.Foreground = Green;
            ChromeDetailLabel.Text = "Debug bridge is active on port 9222.";
            BtnLaunchChrome.IsEnabled = true;
            return;
        }

        ChromeStatusLabel.Text = $"◉ Launching {browserName}...";
        ChromeStatusLabel.Foreground = Cyan;

        try
        {
            var success = await _browserLauncher.LaunchAsync(browserType);
            if (success)
            {
                ChromeStatusLabel.Text = $"◉ {browserName} Ready — Debug bridge active";
                ChromeStatusLabel.Foreground = Green;
                ChromeDetailLabel.Text = $"{browserName} launched. You can now apply to jobs.";
                ChromeDetailLabel.Foreground = Gray;
                BtnLaunchChrome.Content = $"◈ RE-LAUNCH {browserName.ToUpper()}";
            }
            else
            {
                ChromeStatusLabel.Text = "✕ Could not launch browser";
                ChromeStatusLabel.Foreground = Red;
                ChromeDetailLabel.Text = "Select a browser from the list above, or install Chrome/Edge.";
                ChromeDetailLabel.Foreground = Yellow;
                BtnLaunchChrome.Content = "◈ RETRY";
            }
        }
        catch (Exception ex)
        {
            ChromeStatusLabel.Text = $"✕ Error: {ex.Message}";
            ChromeStatusLabel.Foreground = Red;
        }
        finally
        {
            BtnLaunchChrome.IsEnabled = true;
        }
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid id)
        {
            var mainWindow = (MainWindow)Window.GetWindow(this);
            mainWindow.NavigateToApply(id);
        }
    }

    private void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid id)
        {
            var mainWindow = (MainWindow)Window.GetWindow(this);
            mainWindow.NavigateToVault(id);
        }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid id)
        {
            await _profileRepo.DeleteAsync(id);
            await LoadProfilesAsync();
        }
    }
}