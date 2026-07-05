using Envoy.Core.Data;
using Envoy.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Envoy.Core.Services;

public class TailoringEngine
{
    private readonly OllamaService _ollama;
    private readonly SafetyService _safety;
    private readonly IDbContextFactory<EnvoyDbContext> _factory;
    private readonly ILogger<TailoringEngine> _log;

    public TailoringEngine(OllamaService ollama, SafetyService safety, IDbContextFactory<EnvoyDbContext> dbFactory, ILogger<TailoringEngine> log)
    {
        _ollama = ollama;
        _safety = safety;
        _factory = dbFactory;
        _log = log;
    }

    public async Task<TailoredProfile> TailorAsync(Guid masterProfileId, string jobUrl, string jobDescription, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        var master = await db.MasterProfiles
            .Include(p => p.Experience)
            .Include(p => p.Education)
            .Include(p => p.Projects)
            .FirstOrDefaultAsync(p => p.Id == masterProfileId, ct)
            ?? throw new InvalidOperationException("Master profile not found");

        _log.LogInformation("Tailoring profile {ProfileId} for job: {JobUrl}", masterProfileId, jobUrl);

        var tailoredJson = await _ollama.CompleteJsonAsync<MasterProfile>(
            BuildTailoringPrompt(master, jobDescription),
            BuildTailoringSystemPrompt(),
            ct);

        if (tailoredJson == null)
        {
            _log.LogError("LLM returned null/invalid JSON for profile {ProfileId}. Falling back to original.", masterProfileId);
            var failedResult = new TailoredProfile
            {
                MasterProfileId = masterProfileId,
                JobUrl = jobUrl,
                JobDescriptionText = jobDescription,
                TailoredData = master,
                SafetyResult = new SafetyResult
                {
                    Passed = false,
                    Violations = new List<SafetyViolation>
                    {
                        new() { Type = "LowConfidence", Description = "LLM tailoring failed; original profile returned unchanged", Field = "Global" }
                    }
                },
                MatchScore = 0,
                ChangesMade = new List<string> { "Original profile returned (LLM tailoring failed)" }
            };
            db.TailoredProfiles.Add(failedResult);
            await db.SaveChangesAsync(ct);
            return failedResult;
        }

        var tailored = tailoredJson;
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

        if (safetyResult.Passed || !safetyResult.ContainsHallucination)
        {
            result.JobTitle = ExtractJobTitle(jobDescription);
            result.Company = ExtractCompany(jobDescription);
        }

        _log.LogInformation("Tailoring complete: Safety={Passed}, Match={Score:F1}%, Changes={ChangeCount}",
            safetyResult.Passed ? "PASSED" : "FAILED", result.MatchScore, result.ChangesMade.Count);

        db.TailoredProfiles.Add(result);
        await db.SaveChangesAsync(ct);

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
5. If a field has no relevant changes, keep it identical to the original.
6. NEVER change dates, company names, or job titles.
7. NEVER add skills that were not in the original resume.";
    }

    private string BuildTailoringPrompt(MasterProfile master, string jobDescription)
    {
        var masterJson = System.Text.Json.JsonSerializer.Serialize(master, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var truncatedJd = TruncateAtSentenceBoundary(jobDescription, 4000);
        return $"MASTER RESUME:\n{masterJson}\n\nJOB DESCRIPTION:\n{truncatedJd}\n\nRewrite the Master Resume JSON to better match the Job Description. Output ONLY the rewritten JSON.";
    }

    private static string TruncateAtSentenceBoundary(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        var truncated = text[..maxLength];
        var lastSentenceEnd = truncated.LastIndexOfAny(new[] { '.', '!', '?', '\n' });
        if (lastSentenceEnd > maxLength * 0.5)
            return truncated[..(lastSentenceEnd + 1)].Trim();

        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > 0)
            return truncated[..lastSpace].Trim() + "...";

        return truncated + "...";
    }

    private double CalculateMatchScore(MasterProfile tailored, string jobDescription)
    {
        var resumeText = System.Text.Json.JsonSerializer.Serialize(tailored).ToLowerInvariant();
        var jdWords = jobDescription.ToLowerInvariant().Split(new[] { ' ', '\n', '\r', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
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
        var removed = origSkills.Except(newSkills).ToList();
        if (added.Any() || removed.Any())
            changes.Add($"Skills changed: {added.Count} added, {removed.Count} removed");

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
        if (string.IsNullOrWhiteSpace(jobDescription)) return "";

        var lines = jobDescription.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var titleKeywords = new[] { "engineer", "developer", "manager", "analyst", "designer", "director", "lead", "senior", "junior", "intern", "coordinator", "specialist", "architect", "consultant" };

        foreach (var line in lines.Take(10))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 3 && trimmed.Length < 80 && !trimmed.Contains(':', StringComparison.OrdinalIgnoreCase))
            {
                if (titleKeywords.Any(k => trimmed.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    return trimmed;
            }
        }

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
