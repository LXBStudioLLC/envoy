using Envoy.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Envoy.Core.Services;

public class SafetyService
{
    private readonly ILogger<SafetyService> _log;

    public SafetyService(ILogger<SafetyService> log)
    {
        _log = log;
    }

    public SafetyResult ValidateTailoredProfile(MasterProfile original, MasterProfile tailored, string jobDescription)
    {
        var result = new SafetyResult { Passed = true };

        CheckFactualIntegrity(original, tailored, result);
        CheckKeywordDensity(tailored, jobDescription, result);
        CheckLengthConstraint(tailored, result);
        CheckDateConsistency(original, tailored, result);

        if (result.Violations.Any(v => v.Type == "Hallucination"))
        {
            result.ContainsHallucination = true;
            result.Passed = false;
        }

        if (result.Violations.Any(v => v.Type == "KeywordDensity"))
        {
            result.KeywordStuffed = true;
            result.Passed = false;
        }

        if (result.Violations.Any(v => v.Type == "DateInconsistency"))
        {
            result.DateInconsistency = true;
            result.Passed = false;
        }

        _log.LogInformation("Safety validation: {Result} with {Count} violations",
            result.Passed ? "PASSED" : "FAILED", result.Violations.Count);

        return result;
    }

    public bool RequiresSafeMode(SafetyResult result)
    {
        return !result.Passed || result.Violations.Any(v => v.Type is "LowConfidence" or "MissingField");
    }

    private void CheckFactualIntegrity(MasterProfile original, MasterProfile tailored, SafetyResult result)
    {
        var originalSkills = new HashSet<string>(original.Skills, StringComparer.OrdinalIgnoreCase);
        var tailoredSkills = new HashSet<string>(tailored.Skills, StringComparer.OrdinalIgnoreCase);

        var inventedSkills = tailoredSkills.Except(originalSkills).ToList();
        if (inventedSkills.Any())
        {
            result.Violations.Add(new SafetyViolation
            {
                Type = "Hallucination",
                Description = $"Invented skills detected: {string.Join(", ", inventedSkills)}",
                Field = "Skills"
            });
            _log.LogWarning("Hallucination: invented skills {Skills}", string.Join(", ", inventedSkills));
        }

        foreach (var tExp in tailored.Experience)
        {
            var match = original.Experience.FirstOrDefault(o =>
                o.JobTitle.Equals(tExp.JobTitle, StringComparison.OrdinalIgnoreCase) &&
                o.Company.Equals(tExp.Company, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                foreach (var bullet in tExp.Bullets)
                {
                    var isDerivedFromOriginal = match.Bullets.Any(ob =>
                        IsSubstantiallySimilar(bullet, ob));

                    if (!isDerivedFromOriginal)
                    {
                        var longestOriginal = match.Bullets.Max(b => b.Length);
                        if (bullet.Length > longestOriginal * 1.5)
                        {
                            result.Violations.Add(new SafetyViolation
                            {
                                Type = "Hallucination",
                                Description = $"Bullet significantly expanded beyond original: {bullet[..Math.Min(50, bullet.Length)]}...",
                                Field = $"Experience.{tExp.JobTitle}"
                            });
                            _log.LogWarning("Hallucination: expanded bullet in {JobTitle}", tExp.JobTitle);
                        }
                        else
                        {
                            result.Violations.Add(new SafetyViolation
                            {
                                Type = "Hallucination",
                                Description = $"Bullet not derived from original: {bullet[..Math.Min(50, bullet.Length)]}...",
                                Field = $"Experience.{tExp.JobTitle}"
                            });
                            _log.LogWarning("Hallucination: underived bullet in {JobTitle}", tExp.JobTitle);
                        }
                    }
                }
            }
            else
            {
                var originalCompanies = string.Join(", ", original.Experience.Select(e => e.Company));
                result.Violations.Add(new SafetyViolation
                {
                    Type = "Hallucination",
                    Description = $"Experience entry '{tExp.JobTitle} at {tExp.Company}' not found in original resume",
                    Field = $"Experience.{tExp.JobTitle}"
                });
                _log.LogWarning("Hallucination: experience entry '{JobTitle} at {Company}' not in original", tExp.JobTitle, tExp.Company);
            }
        }

        foreach (var tEdu in tailored.Education)
        {
            var eduMatch = original.Education.FirstOrDefault(e =>
                e.Degree.Equals(tEdu.Degree, StringComparison.OrdinalIgnoreCase) &&
                e.Institution.Equals(tEdu.Institution, StringComparison.OrdinalIgnoreCase));

            if (eduMatch == null && !string.IsNullOrWhiteSpace(tEdu.Degree))
            {
                var hasSimilar = original.Education.Any(e =>
                    e.Institution.Equals(tEdu.Institution, StringComparison.OrdinalIgnoreCase) ||
                    e.Degree.Equals(tEdu.Degree, StringComparison.OrdinalIgnoreCase));

                if (!hasSimilar)
                {
                    result.Violations.Add(new SafetyViolation
                    {
                        Type = "Hallucination",
                        Description = $"Education entry '{tEdu.Degree} at {tEdu.Institution}' not found in original resume",
                        Field = $"Education.{tEdu.Institution}"
                    });
                }
            }
        }
    }

    private static bool IsSubstantiallySimilar(string candidate, string original)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(original))
            return false;

        var candNorm = NormalizeForComparison(candidate);
        var origNorm = NormalizeForComparison(original);

        if (candNorm == origNorm)
            return true;

        if (candNorm.Contains(origNorm, StringComparison.OrdinalIgnoreCase))
            return true;

        if (origNorm.Contains(candNorm, StringComparison.OrdinalIgnoreCase))
            return true;

        var overlapWords = candNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Intersect(origNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase)
            .Count();
        var shorterWordCount = Math.Min(
            candNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            origNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);

        if (shorterWordCount > 0 && (double)overlapWords / shorterWordCount >= 0.5)
            return true;

        return false;
    }

    private static string NormalizeForComparison(string text)
    {
        return Regex.Replace(text.ToLowerInvariant().Trim(), @"\s+", " ");
    }

    private void CheckKeywordDensity(MasterProfile tailored, string jobDescription, SafetyResult result)
    {
        var jdWords = ExtractKeywords(jobDescription);
        if (jdWords.Count == 0) return;

        var resumeText = SerializeResume(tailored);
        var resumeWords = resumeText.Split(new[] { ' ', '\n', '\r', '.' }, StringSplitOptions.RemoveEmptyEntries);

        if (resumeWords.Length == 0) return;

        var matchCount = jdWords.Count(jd => resumeWords.Any(r => r.Contains(jd, StringComparison.OrdinalIgnoreCase)));
        var density = (double)matchCount / jdWords.Count;

        if (density > 0.50)
        {
            result.Violations.Add(new SafetyViolation
            {
                Type = "KeywordDensity",
                Description = $"Keyword density {density:P0} exceeds 50%. Risk of keyword stuffing.",
                Field = "Global"
            });
            _log.LogWarning("Keyword density {Density:P0} exceeds 50%", density);
        }
    }

    private void CheckLengthConstraint(MasterProfile tailored, SafetyResult result)
    {
        var text = SerializeResume(tailored);
        var estimatedPages = text.Length / 3000.0;

        if (estimatedPages > 1.2)
        {
            result.Violations.Add(new SafetyViolation
            {
                Type = "Length",
                Description = $"Estimated {estimatedPages:F1} pages. Must fit exactly one page.",
                Field = "Global"
            });
            result.ExceedsLength = true;
        }
    }

    private void CheckDateConsistency(MasterProfile original, MasterProfile tailored, SafetyResult result)
    {
        foreach (var tExp in tailored.Experience)
        {
            if (TryParseDate(tExp.StartDate, out var start) && TryParseDate(tExp.EndDate, out var end))
            {
                if (start > end && !tExp.IsCurrent)
                {
                    result.Violations.Add(new SafetyViolation
                    {
                        Type = "DateInconsistency",
                        Description = $"Start date {tExp.StartDate} is after end date {tExp.EndDate} at {tExp.Company}",
                        Field = $"Experience.{tExp.JobTitle}.Dates"
                    });
                }
            }

            var oExp = original.Experience.FirstOrDefault(o =>
                o.JobTitle.Equals(tExp.JobTitle, StringComparison.OrdinalIgnoreCase) &&
                o.Company.Equals(tExp.Company, StringComparison.OrdinalIgnoreCase));

            if (oExp != null)
            {
                if (tExp.StartDate != oExp.StartDate || tExp.EndDate != oExp.EndDate)
                {
                    var originalDates = $"{oExp.StartDate}–{oExp.EndDate}";
                    var tailoredDates = $"{tExp.StartDate}–{tExp.EndDate}";
                    if (originalDates != tailoredDates)
                    {
                        result.Violations.Add(new SafetyViolation
                        {
                            Type = "DateInconsistency",
                            Description = $"Dates changed from '{originalDates}' to '{tailoredDates}' at {tExp.Company}",
                            Field = $"Experience.{tExp.JobTitle}.Dates"
                        });
                    }
                }
            }
        }
    }

    private static List<string> ExtractKeywords(string text)
    {
        var words = text.Split(new[] { ' ', '\n', '\r', '.', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var stopWords = new HashSet<string> { "the", "and", "for", "with", "you", "are", "will", "must", "have", "this", "that" };
        return words.Where(w => w.Length > 3 && !stopWords.Contains(w.ToLower())).ToList();
    }

    private static string SerializeResume(MasterProfile profile)
    {
        var parts = new List<string>
        {
            profile.Summary ?? "",
            string.Join(" ", profile.Skills)
        };
        foreach (var exp in profile.Experience)
        {
            parts.Add(exp.JobTitle);
            parts.Add(exp.Company);
            parts.AddRange(exp.Bullets);
        }
        return string.Join(" ", parts);
    }

    private static bool TryParseDate(string? dateStr, out DateTime date)
    {
        date = DateTime.MinValue;
        if (string.IsNullOrWhiteSpace(dateStr)) return false;

        var cleaned = Regex.Replace(dateStr, @"[^0-9/\-]", "");
        return DateTime.TryParse(cleaned, out date);
    }
}
