namespace Envoy.Core.Models;

/// <summary>
/// Result of preparing an application: the tailored profile plus the raw job-description
/// text fetched from the posting, so the UI can ghost-score the posting without re-fetching it.
/// </summary>
public record PreparedApplication(TailoredProfile Tailored, string JobDescription);
