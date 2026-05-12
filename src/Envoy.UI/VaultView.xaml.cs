using Envoy.Core.Models;
using Envoy.Core.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Envoy.UI;

public partial class VaultView : UserControl
{
    private readonly IProfileRepository _profileRepo;
    private MasterProfile? _profile;

    private static readonly SolidColorBrush Cyan = new(Color.FromRgb(0x00, 0xF0, 0xFF));
    private static readonly SolidColorBrush Magenta = new(Color.FromRgb(0xFF, 0x00, 0xFF));
    private static readonly SolidColorBrush Green = new(Color.FromRgb(0x39, 0xFF, 0x14));
    private static readonly SolidColorBrush Red = new(Color.FromRgb(0xFF, 0x07, 0x3A));
    private static readonly SolidColorBrush TextFg = new(Color.FromRgb(0xE0, 0xE6, 0xF0));
    private static readonly SolidColorBrush Muted = new(Color.FromRgb(0x88, 0x92, 0xA4));
    private static readonly FontFamily BodyFont = new("pack://application:,,,/Fonts/#JetBrains Mono");

    public VaultView(IProfileRepository profileRepo)
    {
        _profileRepo = profileRepo;
        InitializeComponent();
    }

    public async Task SetProfileId(Guid profileId)
    {
        try
        {
            _profile = await _profileRepo.GetByIdAsync(profileId);
        }
        catch
        {
            _profile = null;
        }

        try
        {
            if (_profile == null)
            {
                NoProfileLabel.Visibility = Visibility.Visible;
                ProfilePanel.Visibility = Visibility.Collapsed;
                return;
            }

            NoProfileLabel.Visibility = Visibility.Collapsed;
            ProfilePanel.Visibility = Visibility.Visible;

            TxtName.Text = _profile.Name;
            TxtEmail.Text = _profile.Email;
            TxtPhone.Text = _profile.Phone;
            TxtLocation.Text = _profile.Location ?? "";
            TxtLinkedIn.Text = _profile.LinkedIn ?? "";
            TxtWebsite.Text = _profile.Website ?? "";
            TxtSummary.Text = _profile.Summary ?? "";
            TxtSkills.Text = string.Join(", ", _profile.Skills);

            var expPanel = new StackPanel();
            foreach (var exp in _profile.Experience)
            {
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x22, 0x35)),
                    CornerRadius = new CornerRadius(3),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x4A)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 0, 0, 8),
                    Effect = new DropShadowEffect { Color = Color.FromRgb(0x00, 0xF0, 0xFF), BlurRadius = 6, ShadowDepth = 0, Opacity = 0.08 }
                };
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = $"{exp.JobTitle} — {exp.Company}", Foreground = TextFg, FontWeight = FontWeights.Bold, FontSize = 13, FontFamily = BodyFont });
                sp.Children.Add(new TextBlock { Text = $"{exp.StartDate} → {exp.EndDate ?? "Present"}", Foreground = Muted, FontSize = 11, FontFamily = BodyFont });
                foreach (var bullet in exp.Bullets)
                    sp.Children.Add(new TextBlock { Text = $"  • {bullet}", Foreground = Muted, FontSize = 11, FontFamily = BodyFont, TextWrapping = TextWrapping.Wrap });
                border.Child = sp;
                expPanel.Children.Add(border);
            }
            ExperienceList.ItemsSource = null;
            ExperienceList.Items.Clear();
            foreach (UIElement child in expPanel.Children)
                ExperienceList.Items.Add(child);

            AnomaliesText.Text = _profile.Anomalies.Any()
                ? string.Join("\n", _profile.Anomalies.Select(a => $"⚠ {a.Field}: {a.Message} [{a.Severity}]"))
                : "✓ NO ANOMALIES DETECTED";
            AnomaliesText.Foreground = _profile.Anomalies.Any()
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0xE6, 0x00))
                : Green;

            SaveStatus.Text = "";
        }
        catch
        {
            NoProfileLabel.Visibility = Visibility.Visible;
            ProfilePanel.Visibility = Visibility.Collapsed;
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_profile == null) return;

        try
        {
            _profile.Name = TxtName.Text;
            _profile.Email = TxtEmail.Text;
            _profile.Phone = TxtPhone.Text;
            _profile.Location = TxtLocation.Text;
            _profile.LinkedIn = TxtLinkedIn.Text;
            _profile.Website = TxtWebsite.Text;
            _profile.Summary = TxtSummary.Text;
            _profile.Skills = TxtSkills.Text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
            _profile.UpdatedAt = DateTime.UtcNow;

            await _profileRepo.UpdateAsync(_profile);
            SaveStatus.Text = "✓ CHANGES COMMITTED SUCCESSFULLY";
            SaveStatus.Foreground = Green;
        }
        catch (Exception ex)
        {
            SaveStatus.Text = $"✕ ERROR: {ex.Message}";
            SaveStatus.Foreground = Red;
        }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        var mainWindow = (MainWindow)Window.GetWindow(this);
        mainWindow.NavigateToDashboard();
    }
}