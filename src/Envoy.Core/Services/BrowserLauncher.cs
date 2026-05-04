using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Envoy.Core.Services;

public interface IBrowserLauncher
{
    Task<List<BrowserInfo>> DetectBrowsersAsync();
    Task<bool> IsRunningWithDebuggingAsync(BrowserType type, int port = 9222);
    Task<bool> LaunchAsync(BrowserType type, int port = 9222);
    BrowserInfo? GetSelectedBrowser();
    void SetSelectedBrowser(BrowserType type);
}

public class BrowserLauncher : IBrowserLauncher
{
    private BrowserType? _selectedBrowser;
    private List<BrowserInfo> _cachedBrowsers = new();

    public async Task<List<BrowserInfo>> DetectBrowsersAsync()
    {
        var browsers = new List<BrowserInfo>();

        var candidates = GetBrowserCandidates();
        foreach (var (type, exePaths, userDataPaths) in candidates)
        {
            var exePath = exePaths.FirstOrDefault(File.Exists);
            if (exePath == null)
            {
                browsers.Add(new BrowserInfo
                {
                    Type = type,
                    DisplayName = GetDisplayName(type),
                    IsInstalled = false
                });
                continue;
            }

            var userDataDir = ResolveUserDataDir(type, exePath);
            var isRunning = await IsRunningWithDebuggingAsync(type);

            browsers.Add(new BrowserInfo
            {
                Type = type,
                DisplayName = GetDisplayName(type),
                ExecutablePath = exePath,
                UserDataDir = userDataDir,
                IsInstalled = true,
                IsRunning = isRunning,
                StealthRating = GetStealthRating(type),
                StealthNote = GetStealthNote(type)
            });
        }

        _cachedBrowsers = browsers;
        return browsers;
    }

    public async Task<bool> IsRunningWithDebuggingAsync(BrowserType type, int port = 9222)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await client.GetAsync($"http://localhost:{port}/json/version");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> LaunchAsync(BrowserType type, int port = 9222)
    {
        var browser = _cachedBrowsers.FirstOrDefault(b => b.Type == type)
            ?? (await DetectBrowsersAsync()).FirstOrDefault(b => b.Type == type);

        if (browser == null || !browser.IsInstalled)
            return false;

        var exePath = browser.ExecutablePath;
        if (string.IsNullOrEmpty(exePath))
            return false;

        var userDataDir = browser.UserDataDir;
        var args = userDataDir != null
            ? $"--remote-debugging-port={port} --user-data-dir=\"{userDataDir}\" --no-first-run --no-default-browser-check --password-store=basic --restore-last-session"
            : $"--remote-debugging-port={port} --no-first-run --no-default-browser-check --password-store=basic";

        if (type == BrowserType.Opera)
        {
            var actualExe = FindOperaActualExe(exePath);
            if (actualExe != null) exePath = actualExe;
        }

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        };

        try
        {
            Process.Start(psi);
            await Task.Delay(3000);
            return await IsRunningWithDebuggingAsync(type, port);
        }
        catch
        {
            return false;
        }
    }

    public BrowserInfo? GetSelectedBrowser() =>
        _selectedBrowser.HasValue
            ? _cachedBrowsers.FirstOrDefault(b => b.Type == _selectedBrowser.Value)
            : null;

    public void SetSelectedBrowser(BrowserType type) => _selectedBrowser = type;

    private static string GetDisplayName(BrowserType type) => type switch
    {
        BrowserType.Chrome => "Google Chrome",
        BrowserType.Edge => "Microsoft Edge",
        BrowserType.Brave => "Brave",
        BrowserType.Opera => "Opera",
        BrowserType.Vivaldi => "Vivaldi",
        _ => type.ToString()
    };

    private static string GetStealthRating(BrowserType type) => type switch
    {
        BrowserType.Chrome => "S",
        BrowserType.Edge => "A",
        BrowserType.Brave => "B+",
        BrowserType.Opera => "B",
        BrowserType.Vivaldi => "C",
        _ => "?"
    };

    private static string GetStealthNote(BrowserType type) => type switch
    {
        BrowserType.Chrome => "Most common browser. Best for stealth.",
        BrowserType.Edge => "High market share. Slightly different User-Agent.",
        BrowserType.Brave => "Built-in shields may block tracking but ad-blocker is detectable.",
        BrowserType.Opera => "Lower market share on job sites. Custom UA string.",
        BrowserType.Vivaldi => "Very low market share. Highly identifiable.",
        _ => ""
    };

    private static List<(BrowserType type, string[] exePaths, string? userDataPath)> GetBrowserCandidates()
    {
        var list = new List<(BrowserType, string[], string?)>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            list.Add((BrowserType.Chrome, new[]
            {
                Path.Combine(pf, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(pf86, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(lad, "Google", "Chrome", "Application", "chrome.exe")
            }, null));

            list.Add((BrowserType.Edge, new[]
            {
                Path.Combine(pf, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(pf86, "Microsoft", "Edge", "Application", "msedge.exe")
            }, null));

            list.Add((BrowserType.Brave, new[]
            {
                Path.Combine(pf, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                Path.Combine(pf86, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                Path.Combine(lad, "BraveSoftware", "Brave-Browser", "Application", "brave.exe")
            }, null));

            list.Add((BrowserType.Opera, new[]
            {
                Path.Combine(lad, "Programs", "Opera", "launcher.exe"),
                Path.Combine(pf, "Opera", "launcher.exe")
            }, null));

            list.Add((BrowserType.Vivaldi, new[]
            {
                Path.Combine(lad, "Vivaldi", "Application", "vivaldi.exe")
            }, null));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            list.Add((BrowserType.Chrome, new[] { "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" }, null));
            list.Add((BrowserType.Edge, new[] { "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge" }, null));
            list.Add((BrowserType.Brave, new[] { "/Applications/Brave Browser.app/Contents/MacOS/Brave Browser" }, null));
            list.Add((BrowserType.Opera, new[] { "/Applications/Opera.app/Contents/MacOS/Opera" }, null));
            list.Add((BrowserType.Vivaldi, new[] { "/Applications/Vivaldi.app/Contents/MacOS/Vivaldi" }, null));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            list.Add((BrowserType.Chrome, new[] { "/usr/bin/google-chrome", "/usr/bin/google-chrome-stable" }, null));
            list.Add((BrowserType.Edge, new[] { "/usr/bin/microsoft-edge", "/usr/bin/microsoft-edge-stable" }, null));
            list.Add((BrowserType.Brave, new[] { "/usr/bin/brave-browser", "/usr/bin/brave" }, null));
            list.Add((BrowserType.Opera, new[] { "/usr/bin/opera" }, null));
            list.Add((BrowserType.Vivaldi, new[] { "/usr/bin/vivaldi", "/usr/bin/vivaldi-stable" }, null));
        }

        return list;
    }

    private static string? ResolveUserDataDir(BrowserType type, string exePath)
    {
        var isEdge = type == BrowserType.Edge || exePath.Contains("Edge", StringComparison.OrdinalIgnoreCase);
        var isBrave = type == BrowserType.Brave || exePath.Contains("Brave", StringComparison.OrdinalIgnoreCase);
        var isOpera = type == BrowserType.Opera || exePath.Contains("Opera", StringComparison.OrdinalIgnoreCase);
        var isVivaldi = type == BrowserType.Vivaldi || exePath.Contains("Vivaldi", StringComparison.OrdinalIgnoreCase);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return type switch
            {
                BrowserType.Chrome => Path.Combine(lad, "Google", "Chrome", "User Data"),
                BrowserType.Edge => Path.Combine(lad, "Microsoft", "Edge", "User Data"),
                BrowserType.Brave => Path.Combine(lad, "BraveSoftware", "Brave-Browser", "User Data"),
                BrowserType.Opera => Path.Combine(lad, "Opera Software", "Opera Stable"),
                BrowserType.Vivaldi => Path.Combine(lad, "Vivaldi", "User Data"),
                _ => null
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return type switch
            {
                BrowserType.Chrome => Path.Combine(home, "Library", "Application Support", "Google", "Chrome"),
                BrowserType.Edge => Path.Combine(home, "Library", "Application Support", "Microsoft Edge"),
                BrowserType.Brave => Path.Combine(home, "Library", "Application Support", "BraveSoftware", "Brave-Browser"),
                BrowserType.Opera => Path.Combine(home, "Library", "Application Support", "com.operasoftware.Opera"),
                BrowserType.Vivaldi => Path.Combine(home, "Library", "Application Support", "Vivaldi"),
                _ => null
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return type switch
            {
                BrowserType.Chrome => Path.Combine(home, ".config", "google-chrome"),
                BrowserType.Edge => Path.Combine(home, ".config", "microsoft-edge"),
                BrowserType.Brave => Path.Combine(home, ".config", "BraveSoftware", "Brave-Browser"),
                BrowserType.Opera => Path.Combine(home, ".config", "opera"),
                BrowserType.Vivaldi => Path.Combine(home, ".config", "vivaldi"),
                _ => null
            };
        }

        return null;
    }

    private static string? FindOperaActualExe(string launcherPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(launcherPath);
            if (dir == null) return null;
            var actualExe = Directory.GetFiles(dir, "opera.exe", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (actualExe != null) return actualExe;
            var parentDir = Path.GetDirectoryName(dir);
            if (parentDir == null) return null;
            return Directory.GetFiles(parentDir, "opera.exe", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch
        {
            return launcherPath;
        }
    }
}