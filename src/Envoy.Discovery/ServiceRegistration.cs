using Envoy.Discovery.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace Envoy.Discovery;

public static class ServiceRegistration
{
    /// <summary>
    /// Registers the job-discovery service and its public-source clients. Every source
    /// gets a typed <see cref="HttpClient"/> with a descriptive User-Agent, JSON Accept
    /// header, and a 20-second timeout (some boards return multi-MB payloads).
    /// </summary>
    public static IServiceCollection AddEnvoyDiscovery(this IServiceCollection services)
    {
        AddAtsSource<GreenhouseSource>(services);
        AddAtsSource<LeverSource>(services);
        AddAtsSource<AshbySource>(services);
        AddAtsSource<WorkableSource>(services);
        AddAtsSource<RecruiteeSource>(services);

        services.AddHttpClient<IWebSearchSource, BraveSearchSource>(ConfigureClient);
        services.AddSingleton<JobDiscoveryService>();
        return services;
    }

    private static void AddAtsSource<T>(IServiceCollection services) where T : class, IAtsBoardSource
    {
        services.AddHttpClient<T>(ConfigureClient);
        // Resolve IAtsBoardSource THROUGH the typed-client registration so the configured
        // HttpClient is injected (same pattern as ghost-detection signal registration).
        services.AddSingleton<IAtsBoardSource>(sp => sp.GetRequiredService<T>());
    }

    private static void ConfigureClient(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Envoy/1.0 (+https://github.com/LXBStudioLLC/envoy)");
    }
}
