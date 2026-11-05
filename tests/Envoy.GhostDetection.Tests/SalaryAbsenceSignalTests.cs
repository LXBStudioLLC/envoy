using Envoy.GhostDetection.Models;
using Envoy.GhostDetection.Signals;
using System.Text.Json;
using Xunit;

namespace Envoy.GhostDetection.Tests;

public class SalaryAbsenceSignalTests
{
    private static readonly SalaryAbsenceSignal Signal = new();

    // A plain description with no pay information at all.
    private const string NoPayJd =
        "We are hiring a Support Engineer to troubleshoot customer issues, coordinate " +
        "with engineering, and keep response times low. Familiarity with ticketing " +
        "systems and SQL is a plus. We offer growth and mentorship in a collaborative team.";

    private static JobPosting Posting(string location, string description, string? salaryText = null) =>
        new() { CompanyName = "Acme Corp", Location = location, DescriptionText = description, SalaryText = salaryText };

    // ── Fires: covered jurisdiction, no salary anywhere ────────────────

    [Fact]
    public async Task EvaluateAsync_CoveredStateNoSalary_FiresWeak()
    {
        var result = await Signal.EvaluateAsync(Posting("Oakland, CA", NoPayJd));

        Assert.NotNull(result);
        Assert.Equal("Salary Absence", result.SignalName);
        Assert.Equal(SignalTier.Weak, result.Tier);
        Assert.True(result.Score <= 0.40, $"Expected a low Weak score, got {result.Score}");
        Assert.True(result.Confidence <= 0.65, $"Expected low-to-moderate confidence, got {result.Confidence}");
        Assert.NotEmpty(result.Evidence);
        Assert.Contains(result.Evidence, line => line.Contains("requires a pay range"));
    }

    [Fact]
    public async Task EvaluateAsync_FullStateName_Fires()
    {
        var result = await Signal.EvaluateAsync(Posting("Denver, Colorado", NoPayJd));
        Assert.NotNull(result);
    }

    [Fact]
    public async Task EvaluateAsync_TrailingStateCode_Fires()
    {
        var result = await Signal.EvaluateAsync(Posting("Seattle, WA", NoPayJd));
        Assert.NotNull(result);
    }

    [Fact]
    public async Task EvaluateAsync_CityWithOwnOrdinance_Fires()
    {
        var result = await Signal.EvaluateAsync(Posting("New York City, NY", NoPayJd));
        Assert.NotNull(result);
    }

    [Fact]
    public async Task EvaluateAsync_FixtureCoveredNoSalary_Fires()
    {
        var fixture = LoadFixture("posting-salary-absent-california.json");
        var result = await Signal.EvaluateAsync(fixture);

        Assert.NotNull(result);
        Assert.Equal(SignalTier.Weak, result.Tier);
        Assert.NotEmpty(result.Evidence);
    }

    // ── Returns null: salary is present ────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_SalaryTextFieldSet_ReturnsNull()
    {
        var result = await Signal.EvaluateAsync(Posting("Oakland, CA", NoPayJd, "$80,000 - $110,000"));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_DollarAmountInDescription_ReturnsNull()
    {
        var description = NoPayJd + " The salary range for this role is $80,000 - $110,000 per year.";
        var result = await Signal.EvaluateAsync(Posting("Oakland, CA", description));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_HourlyRateInDescription_ReturnsNull()
    {
        var description = NoPayJd + " This position pays $35 per hour.";
        var result = await Signal.EvaluateAsync(Posting("Portland, Oregon", description));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_FixtureCoveredWithSalary_ReturnsNull()
    {
        var fixture = LoadFixture("posting-salary-present-california.json");
        var result = await Signal.EvaluateAsync(fixture);
        Assert.Null(result);
    }

    // ── Returns null: location is uncovered or unusable ────────────────

    [Fact]
    public async Task EvaluateAsync_Remote_ReturnsNull()
    {
        var result = await Signal.EvaluateAsync(Posting("Remote", NoPayJd));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_UncoveredState_ReturnsNull()
    {
        var result = await Signal.EvaluateAsync(Posting("Austin, TX", NoPayJd));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_EmptyLocation_ReturnsNull()
    {
        var result = await Signal.EvaluateAsync(Posting("", NoPayJd));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_StateCodeInsideWords_ReturnsNull()
    {
        // "MA" is not a covered state, and a code that merely appears inside
        // another segment must not match. Boston, MA is uncovered either way.
        var result = await Signal.EvaluateAsync(Posting("Boston, MA", NoPayJd));
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_MissingDescription_ReturnsNullForUncovered()
    {
        // No description and no salary text: covered state still fires only on
        // absence of pay anywhere; an uncovered location stays null regardless.
        var result = await Signal.EvaluateAsync(Posting("Remote", ""));
        Assert.Null(result);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static JobPosting LoadFixture(string fileName)
    {
        var assemblyDir = Path.GetDirectoryName(typeof(SalaryAbsenceSignalTests).Assembly.Location)!;
        var fixturePath = Path.Combine(assemblyDir, "..", "..", "..", "fixtures", fileName);
        var raw = File.ReadAllText(fixturePath);
        var stripped = string.Join("\n", raw.Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
        return JsonSerializer.Deserialize<JobPosting>(stripped)!;
    }
}
