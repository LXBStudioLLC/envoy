using Envoy.GhostDetection;
using Envoy.GhostDetection.Signals;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Xunit;

namespace Envoy.GhostDetection.Tests;

/// <summary>
/// Resolves the real DI container. Earlier DI regressions — a missing HttpClient
/// registration, then a typed client that didn't satisfy the IGhostSignal registration —
/// both built and unit-tested green because the other tests construct signals by hand.
/// This test forces every registered IGhostSignal to be built through the container,
/// which is the only place those failures surface.
/// </summary>
public class ContainerResolutionTests
{
    [Fact]
    public void Container_Resolves_GhostScorer_And_All_Signals()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEnvoyGhostDetection();

        using var provider = services.BuildServiceProvider();

        // Resolving GhostScorer pulls IEnumerable<IGhostSignal>, which constructs
        // every signal — including the typed-HttpClient AtsCrossCheckSignal. Before the
        // fix this threw: "Unable to resolve service for type 'System.Net.Http.HttpClient'
        // while attempting to activate 'AtsCrossCheckSignal'".
        var scorer = provider.GetRequiredService<GhostScorer>();
        Assert.NotNull(scorer);

        // The signal that depends on the typed HttpClient must be among the resolved set.
        var signals = provider.GetServices<IGhostSignal>();
        Assert.Contains(signals, s => s.Name == "ATS Cross-Check");
    }

    [Fact]
    public void AtsCrossCheck_resolved_as_IGhostSignal_uses_8s_typed_HttpClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEnvoyGhostDetection();
        using var sp = services.BuildServiceProvider();

        var signal = sp.GetServices<IGhostSignal>().OfType<AtsCrossCheckSignal>().Single();
        var httpField = typeof(AtsCrossCheckSignal)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(f => f.FieldType == typeof(HttpClient));
        var client = (HttpClient)httpField.GetValue(signal)!;

        Assert.Equal(TimeSpan.FromSeconds(8), client.Timeout);
    }
}
