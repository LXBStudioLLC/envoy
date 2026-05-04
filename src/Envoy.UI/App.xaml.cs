using Envoy.Core.Data;
using Envoy.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;

namespace Envoy.UI;

public partial class App
{
    private IHost? _host;

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddEnvoyCore();
                services.AddScoped<IResumePdfGenerator, Envoy.Assets.Pdf.ResumePdfGenerator>();
                services.AddSingleton<MainWindow>();
                services.AddSingleton<DashboardView>();
                services.AddSingleton<ApplyView>();
                services.AddSingleton<VaultView>();
                services.AddSingleton<BrowserSelectionView>();
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