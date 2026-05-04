using Envoy.Core.Models;

namespace Envoy.Core.Configuration;

public class EnvoySettings
{
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public string PreferredModel { get; set; } = "qwen2.5-coder:14b";
    public string ChromeDebuggingPort { get; set; } = "9222";
    public bool AutoLaunchChrome { get; set; } = true;
    public ExecutionMode DefaultMode { get; set; } = ExecutionMode.Stealth;
    public bool CaptureScreenshots { get; set; } = true;
    public string TemplatesPath { get; set; } = "";
    public int TypingSpeedVariance { get; set; } = 35;
    public int MousePathSteps { get; set; } = 25;
    public double RelocationConfidenceThreshold { get; set; } = 0.75;
}
