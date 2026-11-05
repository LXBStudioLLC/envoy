using Envoy.GhostDetection.Models;
using Envoy.GhostDetection.Signals;
using System.Text.Json;
using Xunit;

namespace Envoy.GhostDetection.Tests;

public class ThinDescriptionSignalTests
{
    private static readonly ThinDescriptionSignal Signal = new();

    // A normal, substantive description — the kind a real, active posting has.
    private const string SubstantiveJd =
        "We are seeking a Senior Backend Engineer to design and build the services that " +
        "power our core product. In this role you will own services end to end: modeling " +
        "data, designing APIs, writing the services themselves, and taking them through " +
        "review, deployment, and on-call. Our stack is .NET 8, PostgreSQL, and Kafka, " +
        "running on Kubernetes in AWS. You will work closely with product to scope work " +
        "in small, shippable increments, and with SRE to keep the systems observable and " +
        "reliable. We care about clear written communication, pragmatic architectural " +
        "decisions, and tests that catch real regressions without slowing the team down. " +
        "You will mentor mid-level engineers during code review and pairing, and help us " +
        "raise the bar on the way we design and document systems. Pay for this role is " +
        "$180,000 to $230,000 a year, depending on experience.";

    private static JobPosting Posting(string description) =>
        new() { CompanyName = "Acme Corp", DescriptionText = description };

    // ── Fires: very short description ──────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_VeryShortDescription_FiresWeak()
    {
        var result = await Signal.EvaluateAsync(Posting(
            "We are looking for a Customer Support Representative. Please apply online."));

        Assert.NotNull(result);
        Assert.Equal("Thin Description", result.SignalName);
        Assert.Equal(SignalTier.Weak, result.Tier);
        Assert.True(result.Score <= 0.50, $"Expected a low Weak score, got {result.Score}");
        Assert.True(result.Confidence <= 0.65, $"Expected low-to-moderate confidence, got {result.Confidence}");
        Assert.NotEmpty(result.Evidence);
        Assert.Contains(result.Evidence, line => line.Contains("unusually short"));
    }

    [Fact]
    public async Task EvaluateAsync_ShorterDescription_ScoresHigherButStaysLow()
    {
        var bare = await Signal.EvaluateAsync(Posting("Email us."));
        var middling = await Signal.EvaluateAsync(Posting(new string('x', 300)));

        Assert.NotNull(bare);
        Assert.NotNull(middling);
        Assert.True(bare.Score > middling.Score, "A near-empty description should score above a merely short one");
        Assert.True(bare.Score <= 0.50, "Weak tier must stay capped low");
    }

    [Fact]
    public async Task EvaluateAsync_FixtureShortDescription_Fires()
    {
        var fixture = LoadFixture("posting-thindesc-short.json");
        var result = await Signal.EvaluateAsync(fixture);

        Assert.NotNull(result);
        Assert.Equal(SignalTier.Weak, result.Tier);
        Assert.Contains(result.Evidence, line => line.Contains("unusually short"));
    }

    // ── Fires: almost entirely boilerplate ─────────────────────────────

    [Fact]
    public async Task EvaluateAsync_AlmostAllBoilerplate_FiresWeak()
    {
        var description =
            "We are hiring an Account Manager for our western region. You will own a portfolio " +
            "of mid-market clients and coordinate renewals with our customer success team. " +
            "We are an equal opportunity employer and all qualified applicants will receive " +
            "consideration for employment without regard to race, color, religion, sex, national " +
            "origin, disability, or veteran status. We offer a comprehensive benefits package " +
            "including medical, dental, and vision coverage, retirement plans, paid time off, " +
            "and employee assistance programs. Employment is contingent upon successful " +
            "completion of a background check and, where permitted by state law, a drug screen. " +
            "If you need a reasonable accommodation during the application or hiring process, " +
            "please let us know and we will work with you to meet your needs. We are committed " +
            "to diversity and to building an inclusive workplace where everyone can do their " +
            "best work. To apply, submit your resume and cover letter through our careers page.";

        var result = await Signal.EvaluateAsync(Posting(description));

        Assert.NotNull(result);
        Assert.Equal(SignalTier.Weak, result.Tier);
        Assert.Contains(result.Evidence, line => line.Contains("boilerplate"));
    }

    [Fact]
    public async Task EvaluateAsync_FixtureBoilerplateDescription_Fires()
    {
        var fixture = LoadFixture("posting-thindesc-boilerplate.json");
        var result = await Signal.EvaluateAsync(fixture);

        Assert.NotNull(result);
        Assert.Equal(SignalTier.Weak, result.Tier);
        Assert.NotEmpty(result.Evidence);
    }

    // ── Returns null: substantive or unusable input ────────────────────

    [Fact]
    public async Task EvaluateAsync_SubstantiveDescription_ReturnsNull()
    {
        var result = await Signal.EvaluateAsync(Posting(SubstantiveJd));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_LongDescriptionWithSomeBoilerplate_ReturnsNull()
    {
        // Substantive role content first, standard EEO/benefits tail after —
        // normal for a compliant posting and must not fire.
        var description = SubstantiveJd +
            " We are an equal opportunity employer and all qualified applicants will receive " +
            "consideration for employment without regard to race, color, religion, sex, or " +
            "national origin.";

        var result = await Signal.EvaluateAsync(Posting(description));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_EmptyDescription_ReturnsNull()
    {
        var result = await Signal.EvaluateAsync(Posting(""));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_WhitespaceOnlyDescription_ReturnsNull()
    {
        var result = await Signal.EvaluateAsync(Posting("   \n\t   "));
        Assert.Null(result);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static JobPosting LoadFixture(string fileName)
    {
        var assemblyDir = Path.GetDirectoryName(typeof(ThinDescriptionSignalTests).Assembly.Location)!;
        var fixturePath = Path.Combine(assemblyDir, "..", "..", "..", "fixtures", fileName);
        var raw = File.ReadAllText(fixturePath);
        var stripped = string.Join("\n", raw.Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
        return JsonSerializer.Deserialize<JobPosting>(stripped)!;
    }
}
