using Envoy.Discovery;
using Envoy.Discovery.Sources;
using Envoy.GhostDetection.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Envoy.Discovery.Tests;

/// <summary>
/// Resolves the real DI container for the discovery layer — the same kind of
/// regression guard as the ghost-detection ContainerResolutionTests. Forces every
/// source to be built through its typed HttpClient and the service to be composed.
/// </summary>
public class DiscoveryContainerResolutionTests
{
    [Fact]
    public void AddEnvoyDiscovery_ResolvesServiceAndAllSources()
    {
        var services = new ServiceCollection();
        services.AddEnvoyDiscovery();
        using var provider = services.BuildServiceProvider();

        // Constructs JobDiscoveryService, which pulls IEnumerable<IAtsBoardSource>
        // and IWebSearchSource — each built through its configured typed HttpClient.
        var discovery = provider.GetRequiredService<JobDiscoveryService>();
        Assert.NotNull(discovery);

        var expected = new[] { JobSource.Greenhouse, JobSource.Lever, JobSource.Ashby, JobSource.Workable, JobSource.Recruitee, JobSource.SmartRecruiters };

        var sources = provider.GetServices<IAtsBoardSource>().ToList();
        Assert.Equal(expected.Length, sources.Count);
        Assert.All(expected, ats => Assert.Contains(sources, s => s.Ats == ats));

        var web = provider.GetRequiredService<IWebSearchSource>();
        Assert.Equal("Brave Search", web.Name);

        Assert.NotEmpty(discovery.DefaultBoards);
        Assert.Equal(expected.Length, discovery.SupportedAts.Count);
    }
}
