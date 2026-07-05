namespace Envoy.Core.Services;

/// <summary>
/// Builds the tailored-resume PDF path from untrusted posting fields. The writer
/// (ApplicationOrchestrator) and the reader (TemplateEngine, for the form upload)
/// both call this so the paths always agree, and the components are sanitized so a
/// job posting can't steer the file in or out of the Envoy data folder.
/// </summary>
public static class ResumeFilePath
{
    public static string For(string? name, string? company, string? jobTitle)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Envoy");
        var fileName = SanitizeComponent(name)
            + "_" + SanitizeComponent(company)
            + "_" + SanitizeComponent(jobTitle) + ".pdf";
        return Path.Combine(dir, fileName);
    }

    // Reduce an untrusted display string to a single safe filename component: replace
    // invalid file-name characters (which include the path separators) and spaces with
    // '_', trim leading/trailing dots and underscores, and bound the length.
    public static string SanitizeComponent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
            sb.Append(c == ' ' ? '_' : (Array.IndexOf(invalid, c) >= 0 ? '_' : c));
        var cleaned = sb.ToString().Trim('.', '_');
        if (cleaned.Length == 0) return "unknown";
        return cleaned.Length > 60 ? cleaned[..60] : cleaned;
    }
}
