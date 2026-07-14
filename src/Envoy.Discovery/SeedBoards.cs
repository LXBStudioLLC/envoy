using Envoy.Discovery.Models;
using Envoy.GhostDetection.Models;
using System.Text.Json;

namespace Envoy.Discovery;

/// <summary>
/// A small, user-editable set of public ATS boards Envoy scans out of the box.
/// Loaded from seed-boards.json next to the app; falls back to a built-in default.
/// User-added boards are persisted to %LOCALAPPDATA%\Envoy\seed-boards.json so
/// they survive app updates.
/// </summary>
public static class SeedBoards
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true };

    private sealed record SeedEntry(string Ats, string Token, string? Company);

    /// <summary>The path in %LOCALAPPDATA%\Envoy where user-customized boards are saved.</summary>
    private static readonly string UserBoardPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Envoy", "seed-boards.json");

    public static IReadOnlyList<AtsBoardRef> Load()
    {
        // Try the user's persisted board list first (survives app updates)
        try
        {
            if (File.Exists(UserBoardPath))
            {
                var entries = JsonSerializer.Deserialize<List<SeedEntry>>(File.ReadAllText(UserBoardPath), JsonOpts);
                if (entries is { Count: > 0 })
                {
                    var parsed = ParseEntries(entries);
                    if (parsed.Count > 0) return parsed;
                }
            }
        }
        catch { /* fall through to bundled default */ }

        // Fall back to the bundled seed-boards.json next to the app
        try
        {
            var bundledPath = Path.Combine(AppContext.BaseDirectory, "seed-boards.json");
            if (File.Exists(bundledPath))
            {
                var entries = JsonSerializer.Deserialize<List<SeedEntry>>(File.ReadAllText(bundledPath), JsonOpts);
                if (entries is { Count: > 0 })
                {
                    var parsed = ParseEntries(entries);
                    if (parsed.Count > 0) return parsed;
                }
            }
        }
        catch { /* fall back to built-in default */ }

        return Default;
    }

    /// <summary>Save the current board list to %LOCALAPPDATA%\Envoy\seed-boards.json</summary>
    public static bool Save(IReadOnlyList<AtsBoardRef> boards)
    {
        try
        {
            var dir = Path.GetDirectoryName(UserBoardPath)!;
            Directory.CreateDirectory(dir);
            var entries = boards.Select(b => new SeedEntry(b.Ats.ToString(), b.Token, b.CompanyName)).ToList();
            File.WriteAllText(UserBoardPath, JsonSerializer.Serialize(entries, WriteOpts));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<AtsBoardRef> ParseEntries(List<SeedEntry> entries)
    {
        return entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Token) && Enum.TryParse<JobSource>(e.Ats, true, out _))
            .Select(e => new AtsBoardRef
            {
                Ats = Enum.Parse<JobSource>(e.Ats, true),
                Token = e.Token.Trim(),
                CompanyName = e.Company
            })
            .ToList();
    }

    public static readonly IReadOnlyList<AtsBoardRef> Default = Array.AsReadOnly(new[]
    {
        new AtsBoardRef { Ats = JobSource.Ashby,    Token = "openai",     CompanyName = "OpenAI" },
        new AtsBoardRef { Ats = JobSource.Lever,    Token = "matchgroup", CompanyName = "Match Group" },
        new AtsBoardRef { Ats = JobSource.Lever,    Token = "gopuff",     CompanyName = "Gopuff" },
        new AtsBoardRef { Ats = JobSource.Lever,    Token = "veeva",      CompanyName = "Veeva Systems" },
        new AtsBoardRef { Ats = JobSource.Workable, Token = "viva",       CompanyName = "Viva.com" },
    });
}
