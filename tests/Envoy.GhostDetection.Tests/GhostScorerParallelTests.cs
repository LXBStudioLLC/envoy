using Envoy.GhostDetection;
using Envoy.GhostDetection.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Envoy.Core.Tests;

/// <summary>
/// APP-08: signals evaluate concurrently. Wall-clock time for a batch of
/// delayed signals equals the SLOWEST signal (plus epsilon), not the sum;
/// results pin to the deterministic tier-ordering so evidence is stable; a
/// throwing signal is skipped without sinking the batch.
/// </summary>
public sealed class GhostScorerParallelTests
{
    private sealed class DelayedSignal : IGhostSignal
    {
        private readonly int _delayMs;
        private readonly SignalResult? _result;

        public DelayedSignal(string name, SignalTier tier, int delayMs, SignalResult? result = null)
        {
            Name = name;
            Tier = tier;
            _delayMs = delayMs;
            _result = result;
        }

        public string Name { get; }
        public SignalTier Tier { get; }
        public bool RequiresNetwork => false;

        public async Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default)
        {
            await Task.Delay(_delayMs, ct);
            return _result;
        }
    }

    private sealed class ThrowingSignal : IGhostSignal
    {
        public string Name => "Thrower";
        public SignalTier Tier => SignalTier.Weak;
        public bool RequiresNetwork => false;
        public Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
    }

    private static SignalResult Result(double score, double confidence, string signalName = "s") => new()
    {
        SignalName = signalName,
        Score = score,
        Confidence = confidence,
        Evidence = new[] { "evidence for " + score },
        Tier = SignalTier.Weak
    };

    private static readonly JobPosting Posting = new() { CompanyName = "ACME", JobTitle = "Dev" };

    [Fact]
    public async Task DelayedSignals_Parallel_WallTimeIsSlowestNotSum()
    {
        // 5 signals, all 300 ms, sequential would be ≥1500 ms.
        var delays = new DelayedSignal[]
        {
            new("D1", SignalTier.Weak, 300, Result(0.1, 0.5)),
            new("D2", SignalTier.Weak, 300, Result(0.2, 0.5)),
            new("D3", SignalTier.Weak, 300, Result(0.3, 0.5)),
            new("D4", SignalTier.Weak, 300, Result(0.4, 0.5)),
            new("D5", SignalTier.Weak, 300, Result(0.5, 0.5))
        };
        var scorer = new GhostScorer(delays, Mock.Of<ILogger<GhostScorer>>());

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var score = await scorer.ScoreAsync(Posting);
        sw.Stop();

        Assert.Equal(5, score.Signals.Length);
        Assert.True(sw.ElapsedMilliseconds < 1100,
            $"expected parallel wall-time near 300 ms, got {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task ThrowingSignal_SkippedWithWarning_OthersSurvive()
    {
        var scorer = new GhostScorer(
            new IGhostSignal[]
            {
                new ThrowingSignal(),
                new DelayedSignal("Good", SignalTier.Probabilistic, 1, Result(0.90, 0.95, "Good"))
            },
            Mock.Of<ILogger<GhostScorer>>());

        var score = await scorer.ScoreAsync(Posting);

        Assert.Single(score.Signals);
        Assert.Equal("Good", score.Signals[0].SignalName);
    }

    [Fact]
    public async Task EvidenceOrder_StableDespiteConcurrency()
    {
        // Deterministic tier wins the evidence weighting regardless of who
        // finishes first — the ordering is applied AFTER evaluation.
        var scorer = new GhostScorer(
            new IGhostSignal[]
            {
                new DelayedSignal("Slow deterministic", SignalTier.Deterministic, 80, Result(0.90, 0.90)),
                new DelayedSignal("Fast weak", SignalTier.Weak, 5, Result(0.10, 0.10))
            },
            Mock.Of<ILogger<GhostScorer>>());

        var score = await scorer.ScoreAsync(Posting);

        Assert.StartsWith("evidence for 0.9", score.TopEvidence[0]);
    }

    [Fact]
    public async Task LocalOnlySkipsNetworkSignals_ParallelToo()
    {
        var network = new SkippingNetworkProbe();
        var scorer = new GhostScorer(
            new IGhostSignal[] { network, new DelayedSignal("Local", SignalTier.Weak, 1, Result(0.1, 0.1, "Local")) },
            Mock.Of<ILogger<GhostScorer>>());

        var score = await scorer.ScoreAsync(Posting, localOnly: true);

        Assert.False(network.Evaluated);
        Assert.Single(score.Signals);
        Assert.Equal("Local", score.Signals[0].SignalName);
    }

    private sealed class SkippingNetworkProbe : IGhostSignal
    {
        public string Name => "NetworkProbe";
        public SignalTier Tier => SignalTier.Deterministic;
        public bool RequiresNetwork => true;
        public bool Evaluated { get; private set; }

        public Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default)
        {
            Evaluated = true;
            return Task.FromResult<SignalResult?>(null);
        }
    }
}
