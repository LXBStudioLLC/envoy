using Envoy.Core.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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
    private List<BrowserCard> _cards = new();

    private static readonly SolidColorBrush Cyan = new(Color.FromRgb(0x00, 0xF0, 0xFF));
    private static readonly SolidColorBrush Green = new(Color.FromRgb(0x39, 0xFF, 0x14));
    private static readonly SolidColorBrush Red = new(Color.FromRgb(0xFF, 0x07, 0x3A));
    private static readonly SolidColorBrush Muted = new(Color.FromRgb(0x88, 0x92, 0xA4));

    private static readonly Dictionary<BrowserType, string> BrowserIcons = new()
    {
        { BrowserType.Chrome, "\uE97A" },
        { BrowserType.Edge, "\uE96F" },
        { BrowserType.Brave, "\u2694" },
        { BrowserType.Opera, "\u266B" },
        { BrowserType.Vivaldi, "\u2605" }
    };

    public BrowserSelectionView(IBrowserLauncher browserLauncher)
    {
        _browserLauncher = browserLauncher;
        InitializeComponent();
        Loaded += BrowserSelectionView_Loaded;
    }

    private async void BrowserSelectionView_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ScanBrowsersAsync();
        }
        catch { }
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
        if (sender is Border border)
        {
            if (border.DataContext is BrowserCard card && !card.IsSelected)
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(100, 0, 240, 255));
        }
    }

    private void BrowserCard_Leave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            if (border.DataContext is BrowserCard card && !card.IsSelected)
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x4A));
        }
    }

    private async void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ScanBrowsersAsync();
        }
        catch { }
    }
}