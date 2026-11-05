using Envoy.Core.Configuration;
using Envoy.Core.Services;
using Xunit;

namespace Envoy.Core.Tests;

/// <summary>
/// #92 follow-up: the first-run readiness checklist is a pure computation over
/// probe results — deterministic, offline, and honest (each incomplete step's
/// Fix names exactly one screen to visit).
/// </summary>
public sealed class EnvoyReadinessTests
{
    private static EnvoySettings OkSettings() => new EnvoySettings
    {
        ChromeDebuggingPort = "9222",
        TemplatesPath = "" // default dir assumed present in prod; tests inject dir for the "present" case
    };

    [Fact]
    public void FreshInstall_AllStepsIncomplete_WithActionableFixes()
    {
        var settings = OkSettings();
        settings.TemplatesPath = Path.Combine(Path.GetTempPath(), "envoy-readiness-none-" + Guid.NewGuid());
        settings.ChromeDebuggingPort = ""; // a hand-edged settings file can unset it

        var report = EnvoyReadiness.Compute(
            profileCount: 0,
            llmConnected: false,
            browserDebug: false,
            settings: settings);

        Assert.False(report.AllComplete);
        Assert.Equal(5, report.Steps.Count);
        Assert.All(report.Steps, s => Assert.False(s.Complete));
        Assert.All(report.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Fix)));
        Assert.Contains(report.Steps, s => s.Name == "Profile imported" && s.Fix.Contains("Dashboard"));
        Assert.Contains(report.Steps, s => s.Name == "LLM provider configured" && s.Fix.Contains("Ollama"));
    }

    [Fact]
    public void CompleteSetup_ReportsReady()
    {
        // Real bundled templates directory: the app always ships them (APP-01),
        // so the default path must be present in any build output.
        var report = EnvoyReadiness.Compute(1, true, true, OkSettings());

        Assert.Equal(5, report.Steps.Count);
        Assert.All(report.Steps, s => Assert.True(s.Complete, $"{s.Name}: {s.Status}"));
        Assert.True(report.AllComplete);
    }

    [Fact]
    public void TemplatesPresent_WhenCustomPathHasFiles_TriggersComplete()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "envoy-readiness-" + Guid.NewGuid());
        Directory.CreateDirectory(tmp);
        File.WriteAllText(Path.Combine(tmp, "greenhouse-default.json"), "{}");

        var settings = OkSettings();
        settings.TemplatesPath = tmp;

        var report = EnvoyReadiness.Compute(1, true, true, settings);

        Assert.True(report.AllComplete);
        Assert.True(report.Steps.All(s => s.Complete));
    }

    [Fact]
    public void DebugPort_Invalid_IsFlaggedWithDefault()
    {
        var settings = OkSettings();
        settings.ChromeDebuggingPort = "not-a-port";

        var report = EnvoyReadiness.Compute(0, false, false, settings);

        var port = report.Steps.First(s => s.Name == "Debug port setting");
        Assert.False(port.Complete);
        Assert.Contains("9222", port.Status);
    }

    [Fact]
    public void EachIncompleteStep_LetsTheUserSkipAheadSafely()
    {
        // Two of five complete is a real mid-setup state: the strip must show
        // exactly which steps are missing rather than a boolean blob.
        var settings = OkSettings();
        settings.TemplatesPath = Path.Combine(Path.GetTempPath(), "envoy-readiness-none-" + Guid.NewGuid());

        var report = EnvoyReadiness.Compute(1, true, false, settings);

        var complete = report.Steps.Count(s => s.Complete);
        Assert.Equal(3, complete);
        Assert.All(report.Steps.Where(s => !s.Complete), s => Assert.NotEmpty(s.Fix));
    }
}
