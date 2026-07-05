using Envoy.Core.Configuration;
using Envoy.Core.Services;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static Envoy.UI.Theme;

namespace Envoy.UI;

public class LLMProviderCard
{
    public string ProviderId { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public bool IsConnected { get; set; }
    public bool IsActive { get; set; }
    public string StatusText { get; set; } = "";
    public string ModelCountText { get; set; } = "";
    public List<LLMModelInfo> Models { get; set; } = new();
    public string? Error { get; set; }
}

public partial class LLMSettingsView : UserControl
{
    private readonly LLMDetectionService _detection;
    private readonly EnvoySettings _settings;
    private readonly OllamaService _ollamaService;
    private readonly ILogger<LLMSettingsView> _log;
    private List<LLMProviderCard> _cards = new();
    private int _scanInFlight; // 0 = idle, 1 = scanning. Guarded with Interlocked.

    public LLMSettingsView(LLMDetectionService detection, EnvoySettings settings, OllamaService ollamaService, ILogger<LLMSettingsView> log)
    {
        try
        {
            _detection = detection;
            _settings = settings;
            _ollamaService = ollamaService;
            _log = log;
            InitializeComponent();
            Loaded += LLMSettingsView_Loaded;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "LLMSettingsView constructor failed");
            throw;
        }
    }

    private async void LLMSettingsView_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LoadConfig();
            await ScanAllProvidersAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error in LLMSettingsView_Loaded");
            try
            {
                ConnectionStatusLabel.Text = $"Startup error: {ex.Message}";
                ConnectionStatusLabel.Foreground = Red;
            }
            catch { }
        }
    }

    private void LoadConfig()
    {
        try
        {
            OllamaEndpointBox.Text = _settings.OllamaEndpoint ?? "http://localhost:11434";
            LmStudioPortBox.Text = (_settings.LmStudioPort ?? 1234).ToString();

            var activeProvider = _settings.ActiveLLMProvider ?? "ollama";
            var activeModel = _settings.ActiveLLMModel ?? "";
            ActiveProviderLabel.Text = activeProvider.ToUpper();
            ActiveModelLabel.Text = string.IsNullOrEmpty(activeModel) ? "" : $"Model: {activeModel}";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Error loading LLM config");
        }
    }

    private async Task ScanAllProvidersAsync()
    {
        if (Interlocked.CompareExchange(ref _scanInFlight, 1, 0) != 0) return;

        try
        {
            ConnectionStatusLabel.Text = "Scanning for LLM providers...";
            ConnectionStatusLabel.Foreground = Cyan;

            List<LLMConnectionStatus> statuses;
            try
            {
                statuses = await _detection.DetectAllAsync();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "DetectAllAsync threw");
                statuses = new List<LLMConnectionStatus>();
            }

            _cards = statuses.Select(s => new LLMProviderCard
            {
                ProviderId = s.ProviderId,
                ProviderName = s.ProviderName,
                Endpoint = s.Endpoint ?? "",
                IsConnected = s.IsConnected,
                IsActive = s.ProviderId == (_settings.ActiveLLMProvider ?? "ollama"),
                StatusText = s.IsConnected ? $"Connected — {(s.Models?.Count ?? 0)} model(s)" : (s.Error ?? "Not running"),
                ModelCountText = s.IsConnected ? $"{s.Models?.Count ?? 0} model(s) available" : "Not connected",
                Models = s.Models ?? new List<LLMModelInfo>(),
                Error = s.Error
            }).ToList();

            var activeProvider = _settings.ActiveLLMProvider ?? "ollama";

            var connected = _cards.FirstOrDefault(c => c.ProviderId == activeProvider && c.IsConnected);
            if (connected == null)
                connected = _cards.FirstOrDefault(c => c.IsConnected);

            if (connected != null)
            {
                ConnectionStatusLabel.Text = $"Active: {connected.ProviderName} — {connected.ModelCountText}";
                ConnectionStatusLabel.Foreground = Green;
                ActiveProviderLabel.Text = connected.ProviderName.ToUpper();

                var bestModel = connected.Models.OrderByDescending(m => m.SizeBytes ?? 0).FirstOrDefault();
                ActiveModelLabel.Text = bestModel != null ? $"Model: {bestModel.DisplayName}" : "";
            }
            else
            {
                var anyScanned = _cards.Any();
                if (anyScanned)
                {
                    var allOfflineMsg = string.Join(", ", _cards.Where(c => !c.IsConnected).Select(c => $"{c.ProviderName}: {c.StatusText}"));
                    ConnectionStatusLabel.Text = $"No connected providers. {allOfflineMsg}";
                    ConnectionStatusLabel.Foreground = Yellow;
                }
                else
                {
                    ConnectionStatusLabel.Text = "No LLM providers detected. Install Ollama or configure a cloud API.";
                    ConnectionStatusLabel.Foreground = Red;
                }
            }

            ProviderList.ItemsSource = _cards;

            var connectedCount = _cards.Count(c => c.IsConnected);
            if (connectedCount > 0)
            {
                NoProviderLabel.Visibility = Visibility.Collapsed;
                ProviderNoteLabel.Visibility = Visibility.Visible;
                ProviderNoteLabel.Text = $"{connectedCount} of {_cards.Count} provider(s) connected. Click ACTIVATE to set active provider.";
                ProviderNoteLabel.Foreground = Green;
            }
            else
            {
                NoProviderLabel.Visibility = Visibility.Visible;
                ProviderNoteLabel.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            ConnectionStatusLabel.Text = $"Detection error: {ex.Message}";
            ConnectionStatusLabel.Foreground = Red;
            _log.LogError(ex, "Error scanning for LLM providers");
        }
        finally
        {
            Interlocked.Exchange(ref _scanInFlight, 0);
        }
    }

    private void ActivateProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string providerId)
        {
            _settings.ActiveLLMProvider = providerId;

            var card = _cards.FirstOrDefault(c => c.ProviderId == providerId);
            if (card != null)
            {
                var bestModel = card.Models.OrderByDescending(m => m.SizeBytes ?? 0).FirstOrDefault();
                if (bestModel != null)
                {
                    _settings.ActiveLLMModel = bestModel.Id;
                    ActiveModelLabel.Text = $"Model: {bestModel.DisplayName}";
                }
            }

            ActiveProviderLabel.Text = (card?.ProviderName ?? providerId).ToUpper();

            foreach (var c in _cards) c.IsActive = c.ProviderId == providerId;
            ProviderList.ItemsSource = null;
            ProviderList.ItemsSource = _cards;

            var saved = _settings.Save();
            SwitchActiveProvider();

            if (saved)
            {
                ConnectionStatusLabel.Text = $"Switched to {card?.ProviderName ?? providerId}";
                ConnectionStatusLabel.Foreground = Green;
            }
            else
            {
                ConnectionStatusLabel.Text = $"Switched to {card?.ProviderName ?? providerId} for now, but could not save (settings.json may be locked).";
                ConnectionStatusLabel.Foreground = Yellow;
            }
        }
    }

    private async void ScanProvider_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn && btn.Tag is string)
            {
                ConnectionStatusLabel.Text = "Rescanning...";
                ConnectionStatusLabel.Foreground = Cyan;
                await ScanAllProvidersAsync();
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error in ScanProvider_Click");
            ConnectionStatusLabel.Text = $"Scan error: {ex.Message}";
            ConnectionStatusLabel.Foreground = Red;
        }
    }

    private async void TestProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string providerId) return;

        try
        {
            ConnectionStatusLabel.Text = $"Testing {providerId} connection...";
            ConnectionStatusLabel.Foreground = Cyan;

            var provider = _detection.CreateActiveProvider(providerId);
            var available = await provider.IsAvailableAsync();

            if (available)
            {
                var models = await provider.ListModelsAsync();
                ConnectionStatusLabel.Text = $"✓ {provider.DisplayName} connected — {models.Count} model(s) found";
                ConnectionStatusLabel.Foreground = Green;

                var card = _cards.FirstOrDefault(c => c.ProviderId == providerId);
                if (card != null)
                {
                    card.IsConnected = true;
                    card.StatusText = $"Connected — {models.Count} model(s)";
                    card.ModelCountText = $"{models.Count} model(s) available";
                    card.Models = models;
                    card.Error = null;
                    ProviderList.ItemsSource = null;
                    ProviderList.ItemsSource = _cards;
                }
            }
            else
            {
                ConnectionStatusLabel.Text = $"✕ {provider.DisplayName} not reachable at {_settings.OllamaEndpoint ?? "default endpoint"}";
                ConnectionStatusLabel.Foreground = Red;
            }
        }
        catch (Exception ex)
        {
            ConnectionStatusLabel.Text = $"Connection test failed: {ex.Message}";
            ConnectionStatusLabel.Foreground = Red;
            _log.LogWarning(ex, "Provider test failed for {Provider}", providerId);
        }
    }

    private void Model_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && fe.DataContext is LLMModelInfo model)
            {
                _settings.ActiveLLMModel = model.Id;
                _settings.ActiveLLMProvider = model.Provider;
                ActiveModelLabel.Text = $"Model: {model.DisplayName}";

                foreach (var c in _cards) c.IsActive = c.ProviderId == model.Provider;
                ProviderList.ItemsSource = null;
                ProviderList.ItemsSource = _cards;

                if (!_settings.Save())
                {
                    ConnectionStatusLabel.Text = "Model selected for now, but could not save (settings.json may be locked).";
                    ConnectionStatusLabel.Foreground = Yellow;
                }
                SwitchActiveProvider();
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Error selecting model");
        }
    }

    private async void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var endpoint = OllamaEndpointBox.Text.Trim();
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                ConfigSaveStatus.Text = "Ollama endpoint must be a full http:// or https:// URL.";
                ConfigSaveStatus.Foreground = Red;
                ConfigSaveStatus.Visibility = Visibility.Visible;
                return;
            }

            var portText = LmStudioPortBox.Text.Trim();
            if (!int.TryParse(portText, out var port) || port < 1 || port > 65535)
            {
                ConfigSaveStatus.Text = "LM Studio port must be a number between 1 and 65535.";
                ConfigSaveStatus.Foreground = Red;
                ConfigSaveStatus.Visibility = Visibility.Visible;
                return;
            }

            _settings.OllamaEndpoint = endpoint;
            _settings.LmStudioPort = port;
            _settings.OpenAIApiKey = OpenAIKeyBox.Password;
            _settings.AnthropicApiKey = AnthropicKeyBox.Password;
            _settings.GeminiApiKey = GeminiKeyBox.Password;

            if (!_settings.Save())
            {
                ConfigSaveStatus.Text = "Could not save — settings.json may be locked. Your changes were NOT stored.";
                ConfigSaveStatus.Foreground = Red;
                ConfigSaveStatus.Visibility = Visibility.Visible;
                return;
            }

            ConfigSaveStatus.Text = "Configuration saved. Rescanning...";
            ConfigSaveStatus.Foreground = Green;
            ConfigSaveStatus.Visibility = Visibility.Visible;

            BtnSaveConfig.IsEnabled = false;
            BtnRescan.IsEnabled = false;

            await ScanAllProvidersAsync();
            SwitchActiveProvider();

            ConfigSaveStatus.Text = "Configuration saved and providers rescanned.";
            BtnSaveConfig.IsEnabled = true;
            BtnRescan.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error saving LLM config");
            ConfigSaveStatus.Text = $"Error: {ex.Message}";
            ConfigSaveStatus.Foreground = Red;
            ConfigSaveStatus.Visibility = Visibility.Visible;
            BtnSaveConfig.IsEnabled = true;
            BtnRescan.IsEnabled = true;
        }
    }

    private async void BtnRescan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnRescan.IsEnabled = false;
            await ScanAllProvidersAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error rescanning");
            ConnectionStatusLabel.Text = $"Rescan error: {ex.Message}";
            ConnectionStatusLabel.Foreground = Red;
        }
        finally
        {
            BtnRescan.IsEnabled = true;
        }
    }

private void SwitchActiveProvider()
    {
        try
        {
            var provider = _detection.CreateActiveProvider();
            _ollamaService.SwitchProvider(provider);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to switch active LLM provider");
        }
    }
}