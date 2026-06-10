using Envoy.GhostDetection;
using Envoy.GhostDetection.Signals;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Envoy.GhostDetection;

public static class ServiceRegistration
{
    /// <summary>
    /// Registers <see cref="GhostScorer"/> and every concrete implementation of
    /// <see cref="IGhostSignal"/> discovered in the calling assembly.
    /// </summary>
    /// <remarks>
    /// Signals requiring <see cref="HttpClient"/> are automatically registered as
    /// typed HttpClients (8-second timeout) so authors never need to touch DI wiring.
    /// </remarks>
    public static IServiceCollection AddEnvoyGhostDetection(this IServiceCollection services)
    {
        services.AddSingleton<GhostScorer>();

        var assembly = Assembly.GetExecutingAssembly();
        var signalTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IGhostSignal).IsAssignableFrom(t))
            .ToList();

        foreach (var type in signalTypes)
        {
            if (HasHttpClientConstructor(type))
            {
                // Use reflection to call the generic AddHttpClient<T>(Action<HttpClient>)
                var method = typeof(HttpClientFactoryServiceCollectionExtensions)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.Name == "AddHttpClient" && m.IsGenericMethodDefinition)
                    .Select(m => new { Method = m, Params = m.GetParameters() })
                    .Where(x => x.Params.Length == 2
                             && x.Params[0].ParameterType == typeof(IServiceCollection)
                             && x.Params[1].ParameterType == typeof(Action<HttpClient>))
                    .Select(x => x.Method)
                    .FirstOrDefault();

                if (method == null)
                    throw new InvalidOperationException("Could not locate AddHttpClient<T>(IServiceCollection, Action<HttpClient>) extension method.");

                var generic = method.MakeGenericMethod(type);
                generic.Invoke(null, new object[] { services, new Action<HttpClient>(c => c.Timeout = TimeSpan.FromSeconds(8)) });

                // Resolve IGhostSignal THROUGH the typed-client registration so the configured
                // 8s HttpClient is injected. Bare AddSingleton(typeof(IGhostSignal), type)
                // activates against the container's DEFAULT HttpClient (unconfigured, ~100s),
                // silently bypassing the typed client. Mocked tests cannot catch this.
                services.AddSingleton(typeof(IGhostSignal), sp => (IGhostSignal)sp.GetRequiredService(type));
            }
            else
            {
                services.AddSingleton(typeof(IGhostSignal), type);
            }
        }

        return services;
    }

    private static bool HasHttpClientConstructor(Type type)
    {
        return type.GetConstructors()
            .Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(HttpClient)));
    }
}
