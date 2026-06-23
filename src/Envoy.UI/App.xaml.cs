using Envoy.Core.Data;
using Envoy.Core.Services;
using Envoy.Discovery;
using Envoy.GhostDetection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Windows;

namespace Envoy.UI;

public partial class App
{
    private IHost? _host;

    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Envoy", "crash.log");

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                File.AppendAllText(CrashLogPath, $"[{DateTime.UtcNow:O}] [FATAL-AppDomain] {ex}\n\n");
                System.Diagnostics.Debug.WriteLine($"[FATAL] Unhandled: {ex}");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            File.AppendAllText(CrashLogPath, $"[{DateTime.UtcNow:O}] [FATAL-UnobservedTask] {args.Exception}\n\n");
            System.Diagnostics.Debug.WriteLine($"[FATAL] Unobserved task: {args.Exception}");
            args.SetObserved();
        };

        DispatcherUnhandledException += (_, args) =>
        {
            File.AppendAllText(CrashLogPath, $"[{DateTime.UtcNow:O}] [FATAL-Dispatcher] {args.Exception}\n\n");
            System.Diagnostics.Debug.WriteLine($"[FATAL] Dispatcher: {args.Exception}");
            args.Handled = true;
        };

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddEnvoyCore();
                services.AddEnvoyGhostDetection();
                services.AddEnvoyDiscovery();
                services.AddScoped<IResumePdfGenerator, Envoy.Assets.Pdf.ResumePdfGenerator>();
                services.AddSingleton<MainWindow>();
                services.AddSingleton<DashboardView>();
                services.AddSingleton<ApplyView>();
                services.AddSingleton<FindJobsView>();
                services.AddSingleton<VaultView>();
                services.AddSingleton<BrowserSelectionView>();
                services.AddSingleton<LLMSettingsView>();
            })
            .Build();

        using (var scope = _host.Services.CreateScope())
        {
            DatabaseInitializer.InitializeAsync(scope.ServiceProvider).GetAwaiter().GetResult();
        }

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}