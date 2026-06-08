using Envoy.GhostDetection.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Envoy.GhostDetection.Tests;

public class GhostScorerTests
{
    [Fact]
    public async Task ScoreAsync_NoSignals_ReturnsNeutral()
    {
        var scorer = new GhostScorer(Array.Empty<IGhostSignal>(), NullLogger<GhostScorer>.Instance);
        var posting = new JobPosting { CompanyName = "Acme", JobTitle = "Engineer" };

        var score = await scorer.ScoreAsync(posting);

        Assert.Equal(RiskBand.Neutral, score.Band);
        Assert.Equal(0.0, score.RiskScore);
        Assert.Empty(score.Signals);
    }

    [Fact]
    public async Task ScoreAsync_DeterministicSignal_HighBand()
    {
        var deterministic = new Mock<IGhostSignal>();
        deterministic.Setup(s => s.Name).Returns("Deterministic");
        deterministic.Setup(s => s.Tier).Returns(SignalTier.Deterministic);
        deterministic.Setup(s => s.EvaluateAsync(It.IsAny<JobPosting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SignalResult
            {
                SignalName = "AtsCrossCheck",
                Score = 0.9,
                Confidence = 0.8,
                Evidence = new[] { "Not found on company ATS" },
                Tier = SignalTier.Deterministic
            });

        var scorer = new GhostScorer(new[] { deterministic.Object }, NullLogger<GhostScorer>.Instance);
        var posting = new JobPosting { CompanyName = "Acme", JobTitle = "Engineer" };

        var score = await scorer.ScoreAsync(posting);

        Assert.Equal(RiskBand.High, score.Band);
        Assert.True(score.RiskScore > 50);
        Assert.Contains("Not found on company ATS", score.TopEvidence);
    }

    [Fact]
    public async Task ScoreAsync_TwoProbabilisticSignals_ElevatedBand()
    {
        var sig1 = new Mock<IGhostSignal>();
        sig1.Setup(s => s.Name).Returns("Prob1");
        sig1.Setup(s => s.Tier).Returns(SignalTier.Probabilistic);
        sig1.Setup(s => s.EvaluateAsync(It.IsAny<JobPosting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SignalResult { SignalName = "Prob1", Score = 0.7, Confidence = 0.8, Evidence = new[] { "E1" }, Tier = SignalTier.Probabilistic });

        var sig2 = new Mock<IGhostSignal>();
        sig2.Setup(s => s.Name).Returns("Prob2");
        sig2.Setup(s => s.Tier).Returns(SignalTier.Probabilistic);
        sig2.Setup(s => s.EvaluateAsync(It.IsAny<JobPosting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SignalResult { SignalName = "Prob2", Score = 0.65, Confidence = 0.75, Evidence = new[] { "E2" }, Tier = SignalTier.Probabilistic });

        var scorer = new GhostScorer(new[] { sig1.Object, sig2.Object }, NullLogger<GhostScorer>.Instance);
        var posting = new JobPosting { CompanyName = "Acme", JobTitle = "Engineer" };

        var score = await scorer.ScoreAsync(posting);

        Assert.Equal(RiskBand.Elevated, score.Band);
        Assert.True(score.RiskScore > 0);
    }

    [Fact]
    public async Task ScoreAsync_SignalThrows_IsSkipped()
    {
        var flaky = new Mock<IGhostSignal>();
        flaky.Setup(s => s.Name).Returns("Flaky");
        flaky.Setup(s => s.EvaluateAsync(It.IsAny<JobPosting>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var good = new Mock<IGhostSignal>();
        good.Setup(s => s.Name).Returns("Good");
        good.Setup(s => s.Tier).Returns(SignalTier.Probabilistic);
        good.Setup(s => s.EvaluateAsync(It.IsAny<JobPosting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SignalResult { SignalName = "Good", Score = 0.5, Confidence = 0.5, Evidence = new[] { "OK" }, Tier = SignalTier.Probabilistic });

        var scorer = new GhostScorer(new[] { flaky.Object, good.Object }, NullLogger<GhostScorer>.Instance);
        var posting = new JobPosting { CompanyName = "Acme", JobTitle = "Engineer" };

        var score = await scorer.ScoreAsync(posting);

        Assert.Equal(RiskBand.Neutral, score.Band);
        Assert.Single(score.Signals);
    }
}
