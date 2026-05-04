using Envoy.Core.Data;
using Envoy.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Envoy.Core.Services;

public class TailoringEngine
{
    private readonly OllamaService _ollama;
    private readonly SafetyService _safety;
    private readonly EnvoyDbContext _db;

    public TailoringEngine(OllamaService ollama, SafetyService safety, EnvoyDbContext db)
    {
        _ollama = ollama;
        _safety = safety;
        _db = db;
    }

    public async Task<TailoredProfile> TailorAsync(Guid masterProfileId, string jobUrl, string jobDescription, CancellationToken ct = default)
    {
        var master = await _db.MasterProfiles
            .Include(p => p.Experience)
            .Include(p => p.Education)
            .Include(p => p.Projects)
            .FirstOrDefaultAsync(p => p.Id == masterProfileId, ct)
            ?? throw new InvalidOperationException("Master profile not found");

        var tailoredJson = await _ollama.CompleteJsonAsync<MasterProfile>(
            BuildTailoringPrompt(master, jobDescription),
            BuildTailoringSystemPrompt(),
            ct);

        var tailored = tailoredJson ?? new MasterProfile();

        var safetyResult = _safety.ValidateTailoredProfile(master, tailored, jobDescription);

        var result = new TailoredProfile
        {
            MasterProfileId = masterProfileId,
            JobUrl = jobUrl,
            JobDescriptionText = jobDescription,
            TailoredData = tailored,
            SafetyResult = safetyResult,
            MatchScore = CalculateMatchScore(tailored, jobDescription),
            ChangesMade = DiffChanges(master, tailored)
        };

        if (safetyResult.Passed)
        {
            result.JobTitle = ExtractJobTitle(jobDescription);
            result.Company = ExtractCompany(jobDescription);
        }

        _db.TailoredProfiles.Add(result);
        await _db.SaveChangesAsync(ct);

        return result;
    }

    private string BuildTailoringSystemPrompt()
    {
        return @"You are an expert Career Coach and Resume Tailor. Your task is to rewrite a resume to better match a specific job description.

CRITICAL RULES:
1. You may ONLY reorganize and rephrase existing facts. You must NEVER invent new skills, jobs, or achievements.
2. Mirror the job description's keywords and terminology naturally.
3. Rewrite the Professional Summary and top 4 experience bullet points to align with the JD.
4. Output ONLY valid JSON matching the input schema exactly.
5. If a field has no relevant changes, keep it identical to the original.";
    }

    private string BuildTailoringPrompt(MasterProfile master, string jobDescription)
    {
        var masterJson = System.Text.Json.JsonSerializer.Serialize(master, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        return $"MASTER RESUME:\n{masterJson}\n\nJOB DESCRIPTION:\n{jobDescription[..Math.Min(jobDescription.Length, 4000)]}\n\nRewrite the Master Resume JSON to better match the Job Description. Output ONLY the rewritten JSON.";
    }

    private double CalculateMatchScore(MasterProfile tailored, string jobDescription)
    {
        var resumeText = System.Text.Json.JsonSerializer.Serialize(tailored).ToLower();
        var jdWords = jobDescription.ToLower().Split(new[] { ' ', '\n', '\r', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .Distinct()
            .ToList();

        if (!jdWords.Any()) return 0;

        var matches = jdWords.Count(w => resumeText.Contains(w));
        return Math.Min(100, (double)matches / jdWords.Count * 100);
    }

    private List<string> DiffChanges(MasterProfile original, MasterProfile tailored)
    {
        var changes = new List<string>();

        if (original.Summary != tailored.Summary)
            changes.Add("Summary rewritten");

        var origSkills = new HashSet<string>(original.Skills, StringComparer.OrdinalIgnoreCase);
        var newSkills = new HashSet<string>(tailored.Skills, StringComparer.OrdinalIgnoreCase);
        var added = newSkills.Except(origSkills).ToList();
        if (added.Any())
            changes.Add($"Skills reordered/emphasized");

        foreach (var tExp in tailored.Experience)
        {
            var oExp = original.Experience.FirstOrDefault(o =>
                o.JobTitle.Equals(tExp.JobTitle, StringComparison.OrdinalIgnoreCase));
            if (oExp != null)
            {
                var changedBullets = tExp.Bullets.Where(b => !oExp.Bullets.Contains(b)).ToList();
                if (changedBullets.Any())
                    changes.Add($"{tExp.JobTitle} bullets rewritten ({changedBullets.Count} changed)");
            }
        }

        return changes;
    }

    private static string ExtractJobTitle(string jobDescription)
    {
        var lines = jobDescription.Split('\n');
        return lines.FirstOrDefault()?.Trim() ?? "";
    }

    private static string ExtractCompany(string jobDescription)
    {
        if (string.IsNullOrWhiteSpace(jobDescription)) return "";

        var lines = jobDescription.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var atPatterns = new[] { " at ", " - ", " — ", " by ", " for " };
        foreach (var line in lines.Take(5))
        {
            var trimmed = line.Trim();
            foreach (var pattern in atPatterns)
            {
                var idx = trimmed.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                if (idx > 0)
                {
                    var after = trimmed[(idx + pattern.Length)..].Trim();
                    var end = after.IndexOfAny(new[] { ',', '.', '|', '·', '-' });
                    return end > 0 ? after[..end].Trim() : after.Trim();
                }
            }
        }

        return lines.FirstOrDefault()?.Trim() ?? "";
    }
}
