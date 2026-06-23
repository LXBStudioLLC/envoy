using Envoy.GhostDetection.Models;
using Envoy.GhostDetection.Signals;
using System.Text.Json;
using Xunit;

namespace Envoy.GhostDetection.Tests;

public class ScamPatternSignalTests
{
    private static readonly ScamPatternSignal Signal = new();

    // A clean, ordinary job description that should never trip a scam pattern.
    private const string CleanJd =
        "We are seeking a software engineer to join our growing team. You will design, " +
        "develop, and maintain high-quality applications. The ideal candidate has strong " +
        "problem-solving skills and experience with modern frameworks. We offer a competitive " +
        "salary, health insurance, and flexible working hours. Apply today through our careers page.";

    private static JobPosting Posting(string description) =>
        new() { CompanyName = "Acme Corp", DescriptionText = description };

    [Fact]
    public async Task EvaluateAsync_EmptyDescription_ReturnsNull()
    {
        var result = await Signal.EvaluateAsync(Posting(""));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_CleanPosting_ReturnsNull()
    {
        var result = await Signal.EvaluateAsync(Posting(CleanJd));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_CryptoPayment_FiresHigh()
    {
        var result = await Signal.EvaluateAsync(Posting(
            "You must pay a refundable deposit in Bitcoin before we send your equipment."));

        Assert.NotNull(result);
        Assert.Equal(SignalTier.Deterministic, result.Tier);
        Assert.True(result.Score >= 0.80, $"Expected Score >= 0.80, got {result.Score}");
        Assert.True(result.Confidence >= 0.70, $"Expected Confidence >= 0.70, got {result.Confidence}");
        Assert.NotEmpty(result.Evidence);
    }

    [Fact]
    public async Task EvaluateAsync_UpfrontTrainingFee_FiresHigh()
    {
        var result = await Signal.EvaluateAsync(Posting(
            "Applicants must pay a $250 training fee to start working with us."));

        Assert.NotNull(result);
        Assert.True(result.Score >= 0.80, $"Expected Score >= 0.80, got {result.Score}");
    }

    [Fact]
    public async Task EvaluateAsync_CheckOverpayment_FiresHigh()
    {
        var result = await Signal.EvaluateAsync(Posting(
            "Once hired, we will mail you a check; deposit the check and wire the difference back."));

        Assert.NotNull(result);
        Assert.True(result.Score >= 0.80, $"Expected Score >= 0.80, got {result.Score}");
    }

    [Fact]
    public async Task EvaluateAsync_BankSsnUpfront_FiresHigh()
    {
        var result = await Signal.EvaluateAsync(Posting(
            "To apply, please provide your bank account number and routing number."));

        Assert.NotNull(result);
        Assert.True(result.Score >= 0.80, $"Expected Score >= 0.80, got {result.Score}");
    }

    [Fact]
    public async Task EvaluateAsync_OffPlatformAlone_FiresButBelowHigh()
    {
        // A single off-platform redirect is suspicious but not proof — it must not
        // reach the Deterministic High threshold (0.80) on its own.
        var result = await Signal.EvaluateAsync(Posting(
            "To apply for this remote role, contact our hiring manager on Telegram."));

        Assert.NotNull(result);
        Assert.Equal(SignalTier.Deterministic, result.Tier);
        Assert.True(result.Score >= 0.65 && result.Score < 0.80,
            $"Expected Score in [0.65, 0.80) for a lone off-platform tell, got {result.Score}");
    }

    [Fact]
    public async Task EvaluateAsync_ConvergingTells_RaiseScore()
    {
        // Off-platform + crypto together should converge above either alone.
        var result = await Signal.EvaluateAsync(Posting(
            "Message us on WhatsApp to apply. New hires buy equipment in Bitcoin and we reimburse you."));

        Assert.NotNull(result);
        Assert.True(result.Score >= 0.90, $"Expected Score >= 0.90 for converging tells, got {result.Score}");
    }

    [Fact]
    public async Task EvaluateAsync_GiftCardPerk_DoesNotFalseFire()
    {
        // "gift cards" as a perk, with pay verbs in a SEPARATE sentence, must not match.
        var result = await Signal.EvaluateAsync(Posting(
            "Perks include monthly gift cards and free lunch. We pay a competitive salary."));

        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_PostOfferBackgroundCheck_DoesNotFalseFire()
    {
        // SSN mentioned, but only in a post-offer background-check context — no upfront ask.
        var result = await Signal.EvaluateAsync(Posting(
            "A standard background check including SSN verification is run after you accept our written offer."));

        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_FixtureScamPosting_FiresHigh()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(ScamPatternSignalTests).Assembly.Location)!;
        var fixturePath = Path.Combine(assemblyDir, "..", "..", "..", "fixtures", "posting-scam-telegram-crypto.json");
        var raw = File.ReadAllText(fixturePath);
        var stripped = string.Join("\n", raw.Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
        var fixture = JsonSerializer.Deserialize<JobPosting>(stripped)!;

        var result = await Signal.EvaluateAsync(fixture);

        Assert.NotNull(result);
        Assert.Equal(SignalTier.Deterministic, result.Tier);
        Assert.True(result.Score >= 0.90, $"Expected Score >= 0.90, got {result.Score}");
        Assert.NotEmpty(result.Evidence);
    }
}
