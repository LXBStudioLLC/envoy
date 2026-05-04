namespace Envoy.Core.Services;

public enum BrowserType
{
    Chrome,
    Edge,
    Brave,
    Opera,
    Vivaldi
}

public class BrowserInfo
{
    public BrowserType Type { get; init; }
    public string DisplayName { get; init; } = "";
    public string ExecutablePath { get; init; } = "";
    public string? UserDataDir { get; init; }
    public bool IsInstalled { get; init; }
    public bool IsRunning { get; init; }
    public string StealthRating { get; init; } = "";
    public string StealthNote { get; init; } = "";
}