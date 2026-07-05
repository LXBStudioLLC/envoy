using Envoy.Core.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Envoy.Core.Configuration;

public class EnvoySettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Envoy", "settings.json");

    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public string PreferredModel { get; set; } = "qwen2.5-coder:14b";
    public string ChromeDebuggingPort { get; set; } = "9222";
    public bool AutoLaunchChrome { get; set; } = true;
    public ExecutionMode DefaultMode { get; set; } = ExecutionMode.Safe;

    /// <summary>
    /// Master gate for human-cadence input emulation (Bezier mouse paths, typing jitter)
    /// in the apply copilot. OFF by default and must be explicitly, deliberately enabled
    /// from the Browser view. When false, the form is filled with plain, direct input and
    /// the Stealth execution mode is not offered.
    /// </summary>
    public bool StealthModeEnabled { get; set; } = false;
    public bool CaptureScreenshots { get; set; } = true;
    public string TemplatesPath { get; set; } = "";
    public int TypingSpeedVariance { get; set; } = 35;
    public int MousePathSteps { get; set; } = 25;
    public double RelocationConfidenceThreshold { get; set; } = 0.75;

    public string? ActiveLLMProvider { get; set; }
    public string? ActiveLLMModel { get; set; }
    public int? LmStudioPort { get; set; } = 1234;

    public string? OpenAIApiKeyEncrypted { get; set; }
    public string? AnthropicApiKeyEncrypted { get; set; }
    public string? GeminiApiKeyEncrypted { get; set; }
    public string? BraveSearchApiKeyEncrypted { get; set; }

    [JsonIgnore]
    public string? OpenAIApiKey
    {
        get => Decrypt(OpenAIApiKeyEncrypted);
        set => OpenAIApiKeyEncrypted = Encrypt(value);
    }

    [JsonIgnore]
    public string? AnthropicApiKey
    {
        get => Decrypt(AnthropicApiKeyEncrypted);
        set => AnthropicApiKeyEncrypted = Encrypt(value);
    }

    [JsonIgnore]
    public string? GeminiApiKey
    {
        get => Decrypt(GeminiApiKeyEncrypted);
        set => GeminiApiKeyEncrypted = Encrypt(value);
    }

    /// <summary>
    /// Optional Brave Search API key (X-Subscription-Token) used by the job-discovery
    /// web-search source. Stored DPAPI-encrypted like the LLM provider keys.
    /// </summary>
    [JsonIgnore]
    public string? BraveSearchApiKey
    {
        get => Decrypt(BraveSearchApiKeyEncrypted);
        set => BraveSearchApiKeyEncrypted = Encrypt(value);
    }

    public static EnvoySettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new EnvoySettings();

        string json;
        try
        {
            json = File.ReadAllText(SettingsPath);
        }
        catch (Exception ex)
        {
            // Transient read failure (file locked by sync/AV/another instance). Do NOT
            // treat this as corruption — the on-disk file is intact and will load next
            // launch. Return session defaults without touching it.
            TryLog($"Load read from {SettingsPath} failed: {ex.Message}");
            return new EnvoySettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<EnvoySettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new EnvoySettings();

            if (TryMigrateLegacyKeys(json, settings))
                settings.Save();

            return settings;
        }
        catch (Exception ex)
        {
            // settings.json exists but won't parse (corruption, partial write, manual
            // edit). Preserve the original so the user's DPAPI-encrypted keys can be
            // recovered instead of being silently reset and then overwritten.
            TryPreserveCorrupt(json, ex);
            return new EnvoySettings();
        }
    }

    /// <summary>Persists settings. Returns false (and logs to envoy.log) if the write failed.</summary>
    public bool Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

            // Write to a temp file then atomically swap it into place. A crash, power
            // loss, or disk-full mid-write can never truncate the live settings.json
            // (which would silently lose every stored API key). File.Replace keeps the
            // previous version as a .bak for recovery.
            var tmp = SettingsPath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(SettingsPath))
                File.Replace(tmp, SettingsPath, SettingsPath + ".bak");
            else
                File.Move(tmp, SettingsPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            // File can be locked by antivirus / OneDrive / another Envoy instance. Don't crash
            // the calling flow. Debug.WriteLine is invisible in Release builds, so record the
            // failure to a log file next to settings where the user can find it after the fact.
            TryLog($"Save to {SettingsPath} failed: {ex.Message}");
            return false;
        }
    }

    // Best-effort diagnostic log next to settings.json. Records save/load/crypto
    // failures without crashing the calling flow. Never throws — logging must not
    // become its own failure path.
    private static void TryLog(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var logPath = Path.Combine(dir, "envoy.log");
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [EnvoySettings] {message}{Environment.NewLine}";
            File.AppendAllText(logPath, line);
        }
        catch
        {
            // Logging is best-effort; swallow everything.
        }
    }

    private static void TryPreserveCorrupt(string json, Exception ex)
    {
        try
        {
            File.WriteAllText(SettingsPath + ".corrupt", json);
        }
        catch
        {
            // best-effort
        }
        TryLog($"Load parse of {SettingsPath} failed ({ex.Message}); backed up to {SettingsPath}.corrupt and reset to defaults");
    }

    private static bool TryMigrateLegacyKeys(string json, EnvoySettings settings)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var hasOpenAi = TryReadPlaintext(root, "OpenAIApiKey", out var openAi);
            var hasAnthropic = TryReadPlaintext(root, "AnthropicApiKey", out var anthropic);
            var hasGemini = TryReadPlaintext(root, "GeminiApiKey", out var gemini);

            if (!hasOpenAi && !hasAnthropic && !hasGemini)
                return false;

            // On non-Windows the encrypt setter would throw, so don't try to migrate the
            // values — but still return true so the caller re-saves the file, which strips
            // the legacy plaintext fields (they aren't part of the serialized model).
            if (OperatingSystem.IsWindows())
            {
                if (settings.OpenAIApiKeyEncrypted is null && hasOpenAi)
                    settings.OpenAIApiKey = openAi;
                if (settings.AnthropicApiKeyEncrypted is null && hasAnthropic)
                    settings.AnthropicApiKey = anthropic;
                if (settings.GeminiApiKeyEncrypted is null && hasGemini)
                    settings.GeminiApiKey = gemini;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadPlaintext(JsonElement root, string property, out string value)
    {
        value = "";
        if (!root.TryGetProperty(property, out var element)) return false;
        if (element.ValueKind != JsonValueKind.String) return false;
        var s = element.GetString();
        if (string.IsNullOrEmpty(s)) return false;
        value = s;
        return true;
    }

    // DPAPI encrypts under the current Windows user account. The ciphertext is
    // only decryptable by the same user on the same machine. Copying
    // settings.json to another user or PC will silently fail to decrypt.
    private static string? Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "API key persistence requires Windows DPAPI. Envoy is a Windows-only application.");

        try
        {
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }
        catch (PlatformNotSupportedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Surface the failure instead of silently dropping the key. The settings
            // save flow catches this and tells the user the save failed, rather than
            // reporting success while the key is quietly lost.
            TryLog($"API key encryption (DPAPI Protect) failed: {ex.Message}");
            throw;
        }
    }

    private static string? Decrypt(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return null;
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            var protectedBytes = Convert.FromBase64String(ciphertext);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            TryLog($"API key decryption (DPAPI Unprotect) failed — key present but unreadable on this user/machine: {ex.Message}");
            return null;
        }
    }
}
