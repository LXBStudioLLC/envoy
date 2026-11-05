using Envoy.Core.Configuration;
using Envoy.Core.Data;
using Envoy.Core.Models;
using Envoy.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Envoy.Core.Tests;

/// <summary>
/// APP-03: the local-LLM tailoring completion must be bounded (a hung model
/// degrades to the fallback resume, never a freeze) and a user cancel must
/// surface immediately. Testable without any browser or network: the provider
/// is a stub that sleeps until cancelled.
/// </summary>
public sealed class TailoringEngineTimeoutTests : IDisposable
{
    private sealed class SleepingProvider : ILLMProvider
    {
        public string ProviderId => "stub";
        public string DisplayName => "Stub";
        public string Description => "sleeps forever";
        public bool RequiresApiKey => false;
        public bool IsLocal => true;

        public Task<bool> IsAvailableAsync() => Task.FromResult(true);
        public Task<List<LLMModelInfo>> ListModelsAsync() => Task.FromResult(new List<LLMModelInfo>());

        public Task<string> CompleteAsync(string prompt, string systemPrompt, string? modelId, CancellationToken ct = default)
        {
            Task.Delay(Timeout.Infinite, ct).GetAwaiter().GetResult(); // hangs until the token fires
            throw new OperationCanceledException(ct);
        }

        public Task<T?> CompleteJsonAsync<T>(string prompt, string systemPrompt, string? modelId, CancellationToken ct = default)
            => CompleteAsync(prompt, systemPrompt, modelId, ct).ContinueWith(static _ => default(T));
    }

    private readonly SqliteConnection _connection;
    private readonly SingletonFactory _dbFactory;

    public TailoringEngineTimeoutTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<EnvoyDbContext>().UseSqlite(_connection).Options;
        _dbFactory = new SingletonFactory(options);
        using var db = _dbFactory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    private sealed class SingletonFactory : IDbContextFactory<EnvoyDbContext>
    {
        private readonly DbContextOptions<EnvoyDbContext> _options;
        public SingletonFactory(DbContextOptions<EnvoyDbContext> options) => _options = options;
        public EnvoyDbContext CreateDbContext() => new(_options);
    }

    public void Dispose() => _connection.Dispose();

    private TailoringEngine Build(EnvoySettings? settings = null)
    {
        var ollama = new OllamaService(new SleepingProvider());
        return new TailoringEngine(ollama, new SafetyService(Mock.Of<ILogger<SafetyService>>()),
            _dbFactory, Mock.Of<ILogger<TailoringEngine>>(), settings);
    }

    [Fact]
    public async Task HungModel_TimesOutToFallback_InsteadOfFreezing()
    {
        var profile = new MasterProfile { Name = "Test User", Email = "t@e.com", Summary = "Senior engineer with strong C# background and cloud scale systems experience." };
        using (var db = _dbFactory.CreateDbContext())
        {
            db.MasterProfiles.Add(profile);
            await db.SaveChangesAsync();
        }

        var settings = new EnvoySettings { LLMCompletionTimeoutSeconds = 1 };
        // Tailor with no cancellation: only the completion cap can save us.
        var result = await Build(settings).TailorAsync(
            profile.Id, "https://boards.greenhouse.io/acme/jobs/9", "Senior C# engineer role. Cloud platforms.", CancellationToken.None);

        Assert.True(!result.SafetyResult.Passed, "fallback should be safety-flagged");
        Assert.Contains("timed out", result.SafetyResult.Violations.Single().Description);
        Assert.Equal(profile.Name, result.TailoredData.Name); // unchanged original resume
    }

    [Fact]
    public async Task UserCancelled_PropagatesImmediately()
    {
        var profile = new MasterProfile { Name = "Test User", Email = "t@e.com" };
        using (var db = _dbFactory.CreateDbContext())
        {
            db.MasterProfiles.Add(profile);
            await db.SaveChangesAsync();
        }

        using var cts = new CancellationTokenSource(50); // user "cancel" fires instantly relative to a hung model

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Build().TailorAsync(profile.Id, "https://boards.greenhouse.io/acme/jobs/9", "Role.", cts.Token));
    }
}
