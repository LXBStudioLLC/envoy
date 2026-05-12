using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Envoy.Core.Services;

public sealed record RelocationEntry(
    string TemplateId,
    string Field,
    string OriginalSelector,
    string NewSelector,
    Fingerprint Fingerprint,
    double Score,
    DateTime Timestamp);

public class RelocationLogger
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Envoy");

    private static readonly string LogPath = Path.Combine(LogDirectory, "relocations.jsonl");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly object _lock = new();
    private readonly ILogger<RelocationLogger>? _log;

    public RelocationLogger(ILogger<RelocationLogger>? log = null)
    {
        _log = log;
    }

    public Task LogAsync(RelocationEntry entry)
    {
        return Task.Run(() =>
        {
            try
            {
                var line = JsonSerializer.Serialize(entry, JsonOptions);

                lock (_lock)
                {
                    Directory.CreateDirectory(LogDirectory);
                    File.AppendAllText(LogPath, line + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                // The relocation log is the feedback loop for community template PRs. If
                // we silently swallow write failures (disk full, perms denied, OneDrive
                // sync locked the file, antivirus blocked it), contributors get nothing
                // to PR against. Surface it.
                _log?.LogWarning(ex, "Failed to write relocation entry for template {TemplateId} field {Field}",
                    entry.TemplateId, entry.Field);
                System.Diagnostics.Debug.WriteLine($"[RelocationLogger] Write failed: {ex.Message}");
            }
        });
    }
}
