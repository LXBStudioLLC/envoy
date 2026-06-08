namespace Envoy.Core.Models;

public class ApplicationLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TailoredProfileId { get; set; }
    public string JobUrl { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string SiteTemplateId { get; set; } = string.Empty;
    public ApplicationStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public byte[]? BeforeScreenshot { get; set; }
    public byte[]? AfterScreenshot { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public ExecutionMode Mode { get; set; }
}

public enum ApplicationStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    RequiresCaptcha,
    Blocked,
    SafeModeStopped
}

public enum ExecutionMode
{
    Stealth,
    Safe
}
