namespace Envoy.Core.Models;

public class MasterProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? LinkedIn { get; set; }
    public string? Website { get; set; }
    public string? Summary { get; set; }
    public List<string> Skills { get; set; } = new();
    public List<ExperienceEntry> Experience { get; set; } = new();
    public List<EducationEntry> Education { get; set; } = new();
    public List<ProjectEntry> Projects { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public double ParseConfidence { get; set; } = 0.0;
    public List<ParseAnomaly> Anomalies { get; set; } = new();
}

public class ExperienceEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string JobTitle { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public List<string> Bullets { get; set; } = new();
}

public class EducationEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Degree { get; set; } = string.Empty;
    public string Institution { get; set; } = string.Empty;
    public string? GraduationDate { get; set; }
    public string? Location { get; set; }
}

public class ProjectEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> Technologies { get; set; } = new();
}

public class ParseAnomaly
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public AnomalySeverity Severity { get; set; }
}

public enum AnomalySeverity
{
    Info,
    Warning,
    Critical
}
