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
        services.AddDbContext<EnvoyDbContext>();

        services.AddScoped<IProfileRepository, ProfileRepository>();
        services.AddScoped<ITailoredProfileRepository, TailoredProfileRepository>();
        services.AddScoped<IApplicationLogRepository, ApplicationLogRepository>();
        services.AddSingleton<IOcrService, TesseractOcrService>();

        services.AddSingleton<HardwareProfiler>();
        services.AddSingleton<OllamaService>(sp =>
        {
            var hw = sp.GetRequiredService<HardwareProfiler>().DetectHardware();
            return new OllamaService(hw.RecommendedModel);
        });
        services.AddSingleton<SafetyService>();
        services.AddSingleton<HumanizationService>();
        services.AddSingleton<CdpBrowserService>();
        services.AddSingleton<IBrowserLauncher, BrowserLauncher>();

        services.AddSingleton<EnvoySettings>();
        services.AddSingleton<RelocationLogger>();
        services.AddSingleton<IBrowserQuery>(sp => new CdpBrowserQueryAdapter(sp.GetRequiredService<CdpBrowserService>()));
        services.AddSingleton<IElementLocator, ElementLocatorService>();

        services.AddSingleton<TemplateEngine>(sp =>
            new TemplateEngine(
                Path.Combine(AppContext.BaseDirectory, "Templates"),
                sp.GetRequiredService<IElementLocator>()));

        services.AddScoped<ResumeParserService>();
        services.AddScoped<TailoringEngine>();
        services.AddScoped<ApplicationOrchestrator>();

        return services;
    }
}
