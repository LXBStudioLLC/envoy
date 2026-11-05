using Envoy.Core.Configuration;
using Envoy.Core.Models;

namespace Envoy.Core.Services;

/// <summary>
/// The first-run readiness checklist (issue #92, APP-16 follow-up): one pure,
/// testable computation that turns a few probe results into a per-step list
/// with a plain-language fix line for anything missing. The Dashboard renders
/// it; every state maps to exactly one screen the user should visit.
/// </summary>
public static class EnvoyReadiness
{
    public sealed record ReadinessStep(
        string Name,
        bool Complete,
        string Status,
        string Fix);

    public sealed record ReadinessReport(
        IReadOnlyList<ReadinessStep> Steps,
        bool AllComplete)
    {
        public bool IsReady => AllComplete;
    }

    /// <summary>
    /// Probe inputs:
    /// profileCount — MasterProfiles in the store;
    /// llmConnected — any provider reachable (cloud key or local Ollama/LM Studio);
    /// browserDebug — a browser is running with the debug port open;
    /// settings — for the templates-path + debug-port checks.
    /// </summary>
    public static ReadinessReport Compute(
        int profileCount,
        bool llmConnected,
        bool browserDebug,
        EnvoySettings settings)
    {
        var steps = new List<ReadinessStep>();

        steps.Add(profileCount > 0
            ? new ReadinessStep("Profile imported", true, $"{profileCount} profile{(profileCount == 1 ? "" : "s")} in the Vault", "")
            : new ReadinessStep("Profile imported", false, "none yet",
                "Drop a PDF on the Dashboard — it becomes your profile."));

        steps.Add(llmConnected
            ? new ReadinessStep("LLM provider configured", true, "a provider is set up in AI MODELS", "")
            : new ReadinessStep("LLM provider configured", false, "none is set up",
                "Install Ollama (or set a cloud key in AI MODELS) so tailoring can run."));

        steps.Add(browserDebug
            ? new ReadinessStep("Browser bridge", true, "debug port open", "")
            : new ReadinessStep("Browser bridge", false, "not running with debugging",
                "LAUNCH BROWSER on the Dashboard — Envoy relaunches with its debug bridge."));

        var templatesDir = string.IsNullOrWhiteSpace(settings.TemplatesPath)
            ? Path.Combine(AppContext.BaseDirectory, "Templates")
            : settings.TemplatesPath;
        var templatesPresent = Directory.Exists(templatesDir)
            && Directory.EnumerateFiles(templatesDir, "*.json").Any();
        steps.Add(templatesPresent
            ? new ReadinessStep("Application templates", true, "bundled templates ship with every build", "")
            : new ReadinessStep("Application templates", false, "none found",
                "Templates ship with the app; if they are missing, reinstall or set TemplatesPath."));

        var debugPortOk = int.TryParse(settings.ChromeDebuggingPort, out var port)
            && port is > 0 and <= 65535;
        var normalizedPort = debugPortOk ? port : 9222;
        steps.Add(debugPortOk
            ? new ReadinessStep("Debug port setting", true, $"port {normalizedPort}", "")
            : new ReadinessStep("Debug port setting", false, "invalid — defaulting to 9222",
                "Fix ChromeDebuggingPort in settings.json (or remove it for the default)."));

        return new ReadinessReport(steps, steps.All(s => s.Complete));
    }
}
