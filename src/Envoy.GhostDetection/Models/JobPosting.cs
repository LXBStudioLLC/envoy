namespace Envoy.GhostDetection.Models;

public enum JobSource
{
    Greenhouse,
    Lever,
    Indeed,
    LinkedIn,
    Workday,
    Ashby,
    Other
}

public class JobPosting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public JobSource Source { get; set; } = JobSource.Other;
    public string CompanyName { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string DescriptionText { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime? PostedAtUtc { get; set; }
    public DateTime? LastUpdatedUtc { get; set; }
    public string? SalaryText { get; set; }
    public string? RawSourceId { get; set; }
    public Dictionary<string, string> Extra { get; set; } = new();
}
