using Envoy.Core.Models;
using System.Text.RegularExpressions;

namespace Envoy.Core.Services;

public class SafetyService
{
    public SafetyResult ValidateTailoredProfile(MasterProfile original, MasterProfile tailored, string jobDescription)
    {
        var result = new SafetyResult { Passed = true };

        CheckFactualIntegrity(original, tailored, result);
        CheckKeywordDensity(tailored, jobDescription, result);
        CheckLengthConstraint(tailored, result);
        CheckDateConsistency(tailored, result);

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
                    if (!match.Bullets.Any(b => bullet.Contains(b, StringComparison.OrdinalIgnoreCase) || b.Contains(bullet, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (bullet.Length > match.Bullets.Max(b => b.Length) * 1.5)
                        {
                            result.Violations.Add(new SafetyViolation
                            {
                                Type = "Hallucination",
                                Description = $"Bullet significantly expanded beyond original: {bullet[..Math.Min(50, bullet.Length)]}...",
                                Field = $"Experience.{tExp.JobTitle}"
                            });
                        }
                    }
                }
            }
        }
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

        if (density > 0.30)
        {
            result.Violations.Add(new SafetyViolation
            {
                Type = "KeywordDensity",
                Description = $"Keyword density {density:P} exceeds 30%. Risk of keyword stuffing.",
                Field = "Global"
            });
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

    private void CheckDateConsistency(MasterProfile tailored, SafetyResult result)
    {
        foreach (var exp in tailored.Experience)
        {
            if (TryParseDate(exp.StartDate, out var start) && TryParseDate(exp.EndDate, out var end))
            {
                if (start > end)
                {
                    result.Violations.Add(new SafetyViolation
                    {
                        Type = "DateInconsistency",
                        Description = $"Start date {exp.StartDate} is after end date {exp.EndDate} at {exp.Company}",
                        Field = $"Experience.{exp.JobTitle}.Dates"
                    });
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
