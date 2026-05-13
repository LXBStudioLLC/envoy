namespace Envoy.Core.Models;

public class TailoredProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MasterProfileId { get; set; }
    public string JobUrl { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string? JobDescriptionText { get; set; }
    public double MatchScore { get; set; }
    public MasterProfile TailoredData { get; set; } = new();
    public List<string> ChangesMade { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public SafetyResult SafetyResult { get; set; } = new();
}

public class SafetyResult
{
    public bool Passed { get; set; }
    public List<SafetyViolation> Violations { get; set; } = new();
    public bool ContainsHallucination { get; set; }
    public bool KeywordStuffed { get; set; }
    public bool ExceedsLength { get; set; }
    public bool DateInconsistency { get; set; }
}

public class SafetyViolation
{
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Field { get; set; }
}
