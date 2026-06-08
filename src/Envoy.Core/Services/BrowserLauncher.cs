using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Envoy.Core.Services;

public interface IBrowserLauncher : IDisposable
{
    Task<List<BrowserInfo>> DetectBrowsersAsync();
    Task<bool> IsRunningWithDebuggingAsync(BrowserType type, int port = 9222);
    Task<bool> IsProcessRunningAsync(BrowserType type);
    Task<bool> LaunchAsync(BrowserType type, int port = 9222);
    BrowserInfo? GetSelectedBrowser();
    void SetSelectedBrowser(BrowserType type);
    BrowserType? GetSelectedBrowserType();
    Task<bool> RestartWithDebuggingAsync(BrowserType type, int port = 9222);
}

public class BrowserLauncher : IBrowserLauncher
{
    // Shared HttpClient; per-call timeouts via CancellationTokenSource.
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private BrowserType? _selectedBrowser;
    private List<BrowserInfo> _cachedBrowsers = new();
    private bool _hasLaunchedThisSession;
    private BrowserType _launchedBrowserType;
    private int _launchAttemptCount;
    private Process? _launchedProcess;
    private readonly ILogger<BrowserLauncher>? _log;
    private bool _disposed;

    public BrowserLauncher(ILogger<BrowserLauncher>? log = null)
    {
        _log = log;
    }

    private static readonly string EnvoyProfileDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Envoy", "BrowserProfile");

    public async Task<List<BrowserInfo>> DetectBrowsersAsync()
    {
        var browsers = new List<BrowserInfo>();
        var candidates = GetBrowserCandidates();

        foreach (var (type, exePaths, _) in candidates)
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
            var isRunningWithDebug = await IsRunningWithDebuggingAsync(type);
            var isProcessRunning = IsProcessRunning(type);

            browsers.Add(new BrowserInfo
            {
                Type = type,
                DisplayName = GetDisplayName(type),
                ExecutablePath = exePath,
                UserDataDir = userDataDir,
                IsInstalled = true,
                IsRunning = isRunningWithDebug,
                IsProcessRunning = isProcessRunning,
                StealthRating = GetStealthRating(type),
                StealthNote = GetStealthNote(type)
            });
        }

        _cachedBrowsers = browsers;

        if (!_selectedBrowser.HasValue)
        {
            var defaultBrowser = browsers.FirstOrDefault(b => b.IsInstalled && b.Type == BrowserType.Chrome)
                ?? browsers.FirstOrDefault(b => b.IsInstalled);
            if (defaultBrowser != null)
                _selectedBrowser = defaultBrowser.Type;
        }

        return browsers;
    }

    public async Task<bool> IsRunningWithDebuggingAsync(BrowserType type, int port = 9222)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var response = await Http.GetAsync($"http://localhost:{port}/json/version", cts.Token);
            if (!response.IsSuccessStatusCode)
                return false;

            var json = await response.Content.ReadAsStringAsync(cts.Token);

            if (type == BrowserType.Edge)
            {
                if (!json.Contains("Edg/", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return json.Contains("Browser", StringComparison.OrdinalIgnoreCase)
                || json.Contains("webkit", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public Task<bool> IsProcessRunningAsync(BrowserType type)
    {
        return Task.FromResult(IsProcessRunning(type));
    }

    public async Task<bool> LaunchAsync(BrowserType type, int port = 9222)
    {
        if (_hasLaunchedThisSession && _launchedBrowserType == type)
        {
            if (await IsRunningWithDebuggingAsync(type, port))
                return true;
        }

        var alreadyOnPort = await IsRunningWithDebuggingAsync(type, port);
        if (alreadyOnPort)
        {
            _hasLaunchedThisSession = true;
            _launchedBrowserType = type;
            return true;
        }

        var browser = _cachedBrowsers.FirstOrDefault(b => b.Type == type)
            ?? (await DetectBrowsersAsync()).FirstOrDefault(b => b.Type == type);

        if (browser == null || !browser.IsInstalled)
            return false;

        var exePath = browser.ExecutablePath;
        if (string.IsNullOrEmpty(exePath))
            return false;

        return await StartNewBrowserInstanceAsync(type, exePath, port);
    }

    public async Task<bool> RestartWithDebuggingAsync(BrowserType type, int port = 9222)
    {
        KillBrowserProcesses(type);

        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(250);
            if (!IsProcessRunning(type))
                break;
        }

        await Task.Delay(500);

        var browser = _cachedBrowsers.FirstOrDefault(b => b.Type == type)
            ?? (await DetectBrowsersAsync()).FirstOrDefault(b => b.Type == type);

        if (browser == null || !browser.IsInstalled || string.IsNullOrEmpty(browser.ExecutablePath))
            return false;

        CleanProfileLockFiles(type);

        await Task.Delay(300);

        return await StartNewBrowserInstanceAsync(type, browser.ExecutablePath, port);
    }

    public BrowserInfo? GetSelectedBrowser() =>
        _selectedBrowser.HasValue
            ? _cachedBrowsers.FirstOrDefault(b => b.Type == _selectedBrowser.Value)
            : null;

    public void SetSelectedBrowser(BrowserType type) => _selectedBrowser = type;

    public BrowserType? GetSelectedBrowserType() => _selectedBrowser;

    private async Task<bool> StartNewBrowserInstanceAsync(BrowserType type, string exePath, int port)
    {
        Directory.CreateDirectory(EnvoyProfileDir);

        if (type == BrowserType.Opera)
        {
            var actualExe = FindOperaActualExe(exePath);
            if (actualExe != null) exePath = actualExe;
        }

        var args = $"--remote-debugging-port={port} --user-data-dir=\"{EnvoyProfileDir}\" --no-first-run --no-default-browser-check --password-store=basic --disable-background-networking --disable-client-side-phishing-detection --disable-default-apps --disable-hang-monitor --disable-popup-blocking --disable-prompt-on-repost --disable-sync --metrics-recording-only --safebrowsing-disable-auto-update";

        if (type == BrowserType.Chrome || type == BrowserType.Edge)
        {
            args += " --restore-last-session";
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
            // Capture the Process so we can clean it up on app shutdown. Caveat: many
            // browsers spawn a "launcher" that exits after forking the real browser
            // process(es). In that case _launchedProcess will report HasExited=true
            // at Dispose time and we won't kill anything — that's intentional, to
            // avoid killing browser windows the user opened separately.
            var proc = Process.Start(psi);
            if (proc != null)
                _launchedProcess = proc;

            _launchAttemptCount++;

            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(750);
                if (await IsRunningWithDebuggingAsync(type, port))
                {
                    _hasLaunchedThisSession = true;
                    _launchedBrowserType = type;
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Failed to start browser {Type} from {Path}", type, exePath);
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            var proc = _launchedProcess;
            if (proc != null && !proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Failed to clean up launched browser process");
        }
        finally
        {
            _launchedProcess?.Dispose();
            _launchedProcess = null;
        }

        GC.SuppressFinalize(this);
    }

    private static bool IsProcessRunning(BrowserType type)
    {
        var processNames = type switch
        {
            BrowserType.Chrome => new[] { "chrome" },
            BrowserType.Edge => new[] { "msedge" },
            BrowserType.Brave => new[] { "brave" },
            BrowserType.Opera => new[] { "opera" },
            BrowserType.Vivaldi => new[] { "vivaldi" },
            _ => Array.Empty<string>()
        };

        try
        {
            return processNames.Any(name => Process.GetProcessesByName(name).Length > 0);
        }
        catch
        {
            return false;
        }
    }

    private void KillBrowserProcesses(BrowserType type)
    {
        var processNames = type switch
        {
            BrowserType.Chrome => new[] { "chrome" },
            BrowserType.Edge => new[] { "msedge" },
            BrowserType.Brave => new[] { "brave" },
            BrowserType.Opera => new[] { "opera" },
            BrowserType.Vivaldi => new[] { "vivaldi" },
            _ => Array.Empty<string>()
        };

        try
        {
            foreach (var name in processNames)
            {
                foreach (var proc in Process.GetProcessesByName(name))
                {
                    try { proc.Kill(); }
                    catch (Exception ex) { _log?.LogDebug(ex, "Could not kill {Process} (pid {Pid})", name, proc.Id); }
                }
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "KillBrowserProcesses({Type}) failed", type);
        }
    }

    private static void CleanProfileLockFiles(BrowserType type)
    {
        if (!Directory.Exists(EnvoyProfileDir))
            return;

        var filesToClean = new[] { "SingletonLock", "lockfile", "SingletonCookie", "SingletonSocket" };
        foreach (var fileName in filesToClean)
        {
            var path = Path.Combine(EnvoyProfileDir, fileName);
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                    break;
                }
                catch { Thread.Sleep(200); }
            }
        }
    }

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