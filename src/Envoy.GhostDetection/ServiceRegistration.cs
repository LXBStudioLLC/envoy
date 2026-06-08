using Envoy.GhostDetection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        var assembly = Assembly.GetExecutingAssembly();
        var signalTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IGhostSignal).IsAssignableFrom(t));

        foreach (var type in signalTypes)
        {
            services.AddSingleton(typeof(IGhostSignal), type);
        }

        return services;
    }
}
