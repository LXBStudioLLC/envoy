using Envoy.Discovery.Models;
using Envoy.GhostDetection.Models;
using System.Text.Json;

namespace Envoy.Discovery;

/// <summary>
/// A small, user-editable set of public ATS boards Envoy scans out of the box.
/// Loaded from seed-boards.json next to the app; falls back to a built-in default.
/// </summary>
public static class SeedBoards
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private sealed record SeedEntry(string Ats, string Token, string? Company);

    public static IReadOnlyList<AtsBoardRef> Load()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "seed-boards.json");
            if (File.Exists(path))
            {
                var entries = JsonSerializer.Deserialize<List<SeedEntry>>(File.ReadAllText(path), JsonOpts);
                if (entries is { Count: > 0 })
                {
                    var parsed = entries
                        .Where(e => !string.IsNullOrWhiteSpace(e.Token) && Enum.TryParse<JobSource>(e.Ats, true, out _))
                        .Select(e => new AtsBoardRef
                        {
                            Ats = Enum.Parse<JobSource>(e.Ats, true),
                            Token = e.Token.Trim(),
                            CompanyName = e.Company
                        })
                        .ToList();
                    if (parsed.Count > 0)
                        return parsed;
                }
            }
        }
        catch { /* fall back to built-in default */ }

        return Default;
    }

    public static readonly IReadOnlyList<AtsBoardRef> Default = new[]
    {
        new AtsBoardRef { Ats = JobSource.Ashby,    Token = "openai",     CompanyName = "OpenAI" },
        new AtsBoardRef { Ats = JobSource.Lever,    Token = "matchgroup", CompanyName = "Match Group" },
        new AtsBoardRef { Ats = JobSource.Lever,    Token = "gopuff",     CompanyName = "Gopuff" },
        new AtsBoardRef { Ats = JobSource.Lever,    Token = "veeva",      CompanyName = "Veeva Systems" },
        new AtsBoardRef { Ats = JobSource.Workable, Token = "viva",       CompanyName = "Viva.com" },
    };
}
