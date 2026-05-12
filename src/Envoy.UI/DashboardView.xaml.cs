using Envoy.Core.Models;
using Envoy.Core.Services;
using Microsoft.Win32;
using System.IO;
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
    private bool _browserAutoLaunched;

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
        try
        {
            await LoadProfilesAsync();
            await RefreshBrowserStatusAsync();
        }
        catch { }
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

    private async Task RefreshBrowserStatusAsync()
    {
        var browserType = _browserLauncher.GetSelectedBrowserType() ?? BrowserType.Chrome;
        var browserName = _browserLauncher.GetSelectedBrowser()?.DisplayName ?? GetBrowserDisplayName(browserType);

        try
        {
            var debugReady = await _browserLauncher.IsRunningWithDebuggingAsync(browserType);
            if (debugReady)
            {
                ChromeStatusLabel.Text = $"◉ {browserName} Ready — Debug bridge active";
                ChromeStatusLabel.Foreground = Green;
                ChromeDetailLabel.Text = $"{browserName} is running with remote debugging on port 9222.";
                ChromeDetailLabel.Foreground = Gray;
                BtnLaunchChrome.Content = $"◈ RE-LAUNCH {browserName.ToUpper()}";
                return;
            }

            var processRunning = await _browserLauncher.IsProcessRunningAsync(browserType);
            if (processRunning)
            {
                if (!_browserAutoLaunched)
                {
                    ChromeStatusLabel.Text = $"◉ {browserName} detected (no debug bridge)";
                    ChromeStatusLabel.Foreground = Yellow;
                    ChromeDetailLabel.Text = $"{browserName} is running but without remote debugging. Click below to restart it with the debug bridge.";
                    ChromeDetailLabel.Foreground = Gray;
                    BtnLaunchChrome.Content = $"◈ RESTART {browserName.ToUpper()} WITH DEBUG";
                }
                else
                {
                    ChromeStatusLabel.Text = $"⚠ {browserName} debug bridge not responding";
                    ChromeStatusLabel.Foreground = Yellow;
                    ChromeDetailLabel.Text = "The browser was launched but the debug bridge is not responding. Try clicking below.";
                    ChromeDetailLabel.Foreground = Gray;
                    BtnLaunchChrome.Content = $"◈ RETRY {browserName.ToUpper()} LAUNCH";
                }
                return;
            }

            if (!_browserAutoLaunched)
            {
                ChromeStatusLabel.Text = $"◉ Launching {browserName} with debug bridge...";
                ChromeStatusLabel.Foreground = Cyan;
                ChromeDetailLabel.Text = $"Envoy needs {browserName} with remote debugging for stealth automation.";
                ChromeDetailLabel.Foreground = Gray;
                BtnLaunchChrome.IsEnabled = false;

                var launched = await _browserLauncher.LaunchAsync(browserType);
                _browserAutoLaunched = true;

                if (launched)
                {
                    ChromeStatusLabel.Text = $"◉ {browserName} Ready — Debug bridge active";
                    ChromeStatusLabel.Foreground = Green;
                    ChromeDetailLabel.Text = $"{browserName} launched successfully. You can now apply to jobs.";
                    ChromeDetailLabel.Foreground = Gray;
                    BtnLaunchChrome.Content = $"◈ RE-LAUNCH {browserName.ToUpper()}";
                }
                else
                {
                    ChromeStatusLabel.Text = $"⚠ Could not start {browserName}";
                    ChromeStatusLabel.Foreground = Yellow;
                    ChromeDetailLabel.Text = $"Install {browserName} or select a different browser, then click below to retry.";
                    ChromeDetailLabel.Foreground = Gray;
                    BtnLaunchChrome.Content = $"◈ LAUNCH {browserName.ToUpper()}";
                }

                BtnLaunchChrome.IsEnabled = true;
                return;
            }

            ChromeStatusLabel.Text = $"⚠ {browserName} not detected";
            ChromeStatusLabel.Foreground = Yellow;
            ChromeDetailLabel.Text = $"Click below to launch {browserName} with the debug bridge.";
            ChromeDetailLabel.Foreground = Gray;
            BtnLaunchChrome.Content = $"◈ LAUNCH {browserName.ToUpper()}";
        }
        catch (Exception ex)
        {
            ChromeStatusLabel.Text = $"✕ Browser error: {ex.Message}";
            ChromeStatusLabel.Foreground = Red;
            ChromeDetailLabel.Text = "Click below to retry launching.";
            ChromeDetailLabel.Foreground = Gray;
            BtnLaunchChrome.Content = "◈ LAUNCH BROWSER";
        }
    }

    private static string GetBrowserDisplayName(BrowserType type) => type switch
    {
        BrowserType.Chrome => "Google Chrome",
        BrowserType.Edge => "Microsoft Edge",
        BrowserType.Brave => "Brave",
        BrowserType.Opera => "Opera",
        BrowserType.Vivaldi => "Vivaldi",
        _ => "Browser"
    };

    private async void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf", Title = "Select Resume PDF" };
        if (dlg.ShowDialog() != true) return;
        await ImportPdfAsync(dlg.FileName);
    }

    private static readonly long MaxResumePdfBytes = 50 * 1024 * 1024; // 50 MB

    private async Task ImportPdfAsync(string pdfPath)
    {
        if (!File.Exists(pdfPath))
        {
            ImportStatus.Text = $"✕ FILE NOT FOUND: {pdfPath}";
            ImportStatus.Foreground = Red;
            return;
        }

        if (!pdfPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            ImportStatus.Text = "✕ ONLY PDF FILES ARE SUPPORTED";
            ImportStatus.Foreground = Red;
            return;
        }

        try
        {
            var size = new FileInfo(pdfPath).Length;
            if (size > MaxResumePdfBytes)
            {
                ImportStatus.Text = $"✕ FILE TOO LARGE ({size / (1024 * 1024)} MB; limit 50 MB)";
                ImportStatus.Foreground = Red;
                return;
            }
        }
        catch
        {
            // Size check is best-effort; if it fails we'll let the parser deal with it.
        }

        BtnImport.IsEnabled = false;
        ImportStatus.Text = "INITIALIZING PARSE SEQUENCE...";
        ImportStatus.Foreground = Cyan;

        try
        {
            var profile = await _orchestrator.ImportResumeAsync(pdfPath);
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

    private void ImportDropZone_DragEnter(object sender, DragEventArgs e)
    {
        ImportDropZone_DragOver(sender, e);
        if (e.Effects != DragDropEffects.None)
            ImportDropZone.BorderBrush = Cyan;
    }

    private void ImportDropZone_DragLeave(object sender, DragEventArgs e)
    {
        ImportDropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x4A));
    }

    private void ImportDropZone_DragOver(object sender, DragEventArgs e)
    {
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        var ok = files?.Length == 1 && files[0].EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void ImportDropZone_Drop(object sender, DragEventArgs e)
    {
        ImportDropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x4A));

        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files == null || files.Length == 0) return;

        if (files.Length > 1)
        {
            ImportStatus.Text = "✕ DROP ONLY ONE PDF AT A TIME";
            ImportStatus.Foreground = Red;
            return;
        }

        await ImportPdfAsync(files[0]);
    }

    private async void BtnLaunchChrome_Click(object sender, RoutedEventArgs e)
    {
        BtnLaunchChrome.IsEnabled = false;
        var browserType = _browserLauncher.GetSelectedBrowserType() ?? BrowserType.Chrome;
        var browserName = _browserLauncher.GetSelectedBrowser()?.DisplayName ?? GetBrowserDisplayName(browserType);

        ChromeStatusLabel.Text = $"◉ Restarting {browserName} with debug bridge...";
        ChromeStatusLabel.Foreground = Cyan;
        ChromeDetailLabel.Text = $"Closing existing {browserName} session and restarting with debugging enabled. Your tabs will be restored.";
        ChromeDetailLabel.Foreground = Gray;

        try
        {
            var success = await _browserLauncher.RestartWithDebuggingAsync(browserType);
            if (success)
            {
                ChromeStatusLabel.Text = $"◉ {browserName} Ready — Debug bridge active";
                ChromeStatusLabel.Foreground = Green;
                ChromeDetailLabel.Text = $"{browserName} restarted with debugging. You can now apply to jobs.";
                ChromeDetailLabel.Foreground = Gray;
                BtnLaunchChrome.Content = $"◈ RE-LAUNCH {browserName.ToUpper()}";
            }
            else
            {
                ChromeStatusLabel.Text = "✕ Could not start browser";
                ChromeStatusLabel.Foreground = Red;
                ChromeDetailLabel.Text = "Select a browser from the list above, or install Chrome/Edge and try again.";
                ChromeDetailLabel.Foreground = Yellow;
                BtnLaunchChrome.Content = "◈ RETRY";
            }
        }
        catch (Exception ex)
        {
            ChromeStatusLabel.Text = $"✕ Error: {ex.Message}";
            ChromeStatusLabel.Foreground = Red;
            ChromeDetailLabel.Text = "Click below to retry.";
            ChromeDetailLabel.Foreground = Gray;
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

    private async void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid id)
        {
            var mainWindow = (MainWindow)Window.GetWindow(this);
            try
            {
                await mainWindow.NavigateToVault(id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NavigateToVault failed for {id}: {ex}");
            }
        }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn && btn.Tag is Guid id)
            {
                await _profileRepo.DeleteAsync(id);
                await LoadProfilesAsync();
            }
        }
        catch (Exception ex)
        {
            ImportStatus.Text = $"✕ DELETE ERROR: {ex.Message}";
            ImportStatus.Foreground = Red;
        }
    }
}