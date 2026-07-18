using Envoy.Core.Data;
using Envoy.Core.Services;
using Envoy.Discovery;
using Envoy.GhostDetection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Windows;

namespace Envoy.UI;

public partial class App
{
    private IHost? _host;

    private static readonly string EnvoyDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Envoy");

    private static readonly string CrashLogPath = Path.Combine(EnvoyDataDir, "crash.log");
    private static readonly string LogDir = Path.Combine(EnvoyDataDir, "logs");

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                File.AppendAllText(CrashLogPath, $"[{DateTime.UtcNow:O}] [FATAL-AppDomain] {ex}\n\n");
                ShowCrashError("Unexpected error", ex, fatal: true);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            File.AppendAllText(CrashLogPath, $"[{DateTime.UtcNow:O}] [UnobservedTask] {args.Exception}\n\n");
            args.SetObserved();
        };

        DispatcherUnhandledException += (_, args) =>
        {
            File.AppendAllText(CrashLogPath, $"[{DateTime.UtcNow:O}] [Dispatcher] {args.Exception}\n\n");
            ShowCrashError("Unexpected error", args.Exception, fatal: false);
            args.Handled = true;
        };

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new Logging.FileLoggerProvider(LogDir));
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddEnvoyCore();
                services.AddEnvoyGhostDetection();
                services.AddEnvoyDiscovery();
                services.AddScoped<IResumePdfGenerator, Envoy.Assets.Pdf.ResumePdfGenerator>();
                services.AddSingleton<MainWindow>();
                services.AddSingleton<ScoreboardView>();
                services.AddSingleton<DashboardView>();
                services.AddSingleton<ApplyView>();
                services.AddSingleton<FindJobsView>();
                services.AddSingleton<VaultView>();
                services.AddSingleton<BrowserSelectionView>();
                services.AddSingleton<LLMSettingsView>();
            })
            .Build();

        try
        {
            using var scope = _host.Services.CreateScope();
            DatabaseInitializer.InitializeAsync(scope.ServiceProvider).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            File.AppendAllText(CrashLogPath, $"[{DateTime.UtcNow:O}] [FATAL-Startup-DbInit] {ex}\n\n");
            ShowCrashError("Envoy couldn't open its local database", ex, fatal: true);
            Shutdown(1);
            return;
        }

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ShowCrashError(string context, Exception ex, bool fatal)
    {
        try
        {
            var head = fatal
                ? "Envoy hit a problem it couldn't recover from and needs to close."
                : "Envoy hit an unexpected error but will try to keep running.";
            var choice = MessageBox.Show(
                $"{head}\n\n{context}: {ex.Message}\n\n" +
                $"Details were saved to:\n{CrashLogPath}\n\n" +
                "Open the logs folder now?",
                fatal ? "Envoy: fatal error" : "Envoy: error",
                MessageBoxButton.YesNo,
                fatal ? MessageBoxImage.Error : MessageBoxImage.Warning);

            if (choice == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = EnvoyDataDir,
                    UseShellExecute = true
                });
            }
        }
        catch { /* a crash handler must never throw */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}