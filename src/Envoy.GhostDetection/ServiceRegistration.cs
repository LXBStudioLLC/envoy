using Envoy.GhostDetection;
using Envoy.GhostDetection.Signals;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Envoy.GhostDetection;

public static class ServiceRegistration
{
    /// <summary>
    /// Registers <see cref="GhostScorer"/> and every concrete implementation of
    /// <see cref="IGhostSignal"/> discovered in the calling assembly (or the
    /// <c>Envoy.GhostDetection</c> assembly if not invoked from there).
    /// </summary>
    public static IServiceCollection AddEnvoyGhostDetection(this IServiceCollection services)
    {
        services.AddSingleton<GhostScorer>();

        // Typed HttpClient for AtsCrossCheckSignal (8-second timeout)
        services.AddHttpClient<AtsCrossCheckSignal>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(8);
        });

        var assembly = Assembly.GetExecutingAssembly();
        var signalTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IGhostSignal).IsAssignableFrom(t));

        foreach (var type in signalTypes)
        {
            // AtsCrossCheckSignal is registered above as a typed HttpClient client.
            // Resolve its IGhostSignal through that registration so the configured
            // HttpClient is injected. Default activation (AddSingleton(IGhostSignal, type))
            // would try to resolve a bare HttpClient, which the typed-client registration
            // does NOT provide — and that throws the moment GhostScorer pulls
            // IEnumerable<IGhostSignal>.
            if (type == typeof(AtsCrossCheckSignal))
                services.AddSingleton<IGhostSignal>(sp => sp.GetRequiredService<AtsCrossCheckSignal>());
            else
                services.AddSingleton(typeof(IGhostSignal), type);
        }

        return services;
    }
}
