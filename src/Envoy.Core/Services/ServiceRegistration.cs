using Envoy.Core.Configuration;
using Envoy.Core.Data;
using Envoy.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Envoy.Core.Services;

public static class ServiceRegistration
{
    public static IServiceCollection AddEnvoyCore(this IServiceCollection services)
    {
        // A factory (not a scoped context) so the singleton views that hold repositories
        // never capture a long-lived DbContext. Each repository creates a short-lived
        // context per operation, so there's no shared change tracker and no concurrent-
        // access crash.
        services.AddDbContextFactory<EnvoyDbContext>();

        services.AddSingleton<IProfileRepository, ProfileRepository>();
        services.AddSingleton<ITailoredProfileRepository, TailoredProfileRepository>();
        services.AddSingleton<IApplicationLogRepository, ApplicationLogRepository>();
        services.AddSingleton<IJobEventRepository, JobEventRepository>();
        services.AddSingleton<IOcrService, TesseractOcrService>();

        services.AddSingleton<HardwareProfiler>();
        services.AddSingleton<SafetyService>();
        services.AddSingleton<HumanizationService>();
        services.AddSingleton<CdpBrowserService>();
        services.AddSingleton<IBrowserLauncher, BrowserLauncher>();

        services.AddSingleton(EnvoySettings.Load());
        services.AddSingleton<IUpdateCheckService>(sp =>
            new UpdateCheckService(new HttpClient(), sp.GetRequiredService<ILogger<UpdateCheckService>>()));
        services.AddSingleton<LLMDetectionService>();
        services.AddSingleton<OllamaService>(sp =>
        {
            var detection = sp.GetRequiredService<LLMDetectionService>();
            var log = sp.GetRequiredService<ILogger<OllamaService>>();
            var provider = detection.CreateActiveProvider();
            return new OllamaService(provider, log);
        });

        services.AddSingleton<RelocationLogger>();
        services.AddSingleton<IBrowserQuery>(sp => new CdpBrowserQueryAdapter(sp.GetRequiredService<CdpBrowserService>()));
        services.AddSingleton<IElementLocator, ElementLocatorService>();

        services.AddSingleton<TemplateEngine>(sp =>
            new TemplateEngine(
                Path.Combine(AppContext.BaseDirectory, "Templates"),
                sp.GetRequiredService<IElementLocator>()));

        services.AddSingleton<ResumeParserService>();
        services.AddSingleton<TailoringEngine>();
        services.AddSingleton<ApplicationOrchestrator>();

        return services;
    }
}