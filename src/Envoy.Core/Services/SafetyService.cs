using Envoy.Core.Models;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Envoy.Core.Services;

public class SafetyService
{
    private readonly ILogger<SafetyService> _log;

    // Stopwords excluded from overlap counting so that "the", "and", "for"
    // don't trick the heuristic into calling two unrelated bullets similar.
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "but", "if", "to", "of", "in", "on", "at", "by", "for",
        "with", "from", "as", "is", "are", "was", "were", "be", "been", "being", "have", "has",
        "had", "do", "does", "did", "will", "would", "should", "could", "may", "might", "must",
        "i", "you", "we", "they", "he", "she", "it", "that", "this", "these", "those",
        "my", "your", "our", "their", "his", "her", "its"
    };

    private static readonly string[] DateFormats = new[]
    {
        "MMM yyyy", "MMMM yyyy",
        "MMM-yyyy", "MMMM-yyyy",
        "MMM, yyyy", "MMMM, yyyy",
        "yyyy-MM", "yyyy/MM",
        "MM/yyyy", "M/yyyy",
        "yyyy",
        "MM/dd/yyyy", "M/d/yyyy", "M/dd/yyyy", "MM/d/yyyy",
        "yyyy-MM-dd", "yyyy/MM/dd"
    };

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

        var candContent = candNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !StopWords.Contains(w))
            .ToArray();
        var origContent = origNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !StopWords.Contains(w))
            .ToArray();

        if (candContent.Length == 0 || origContent.Length == 0)
            return false;

        var overlapWords = candContent.Intersect(origContent, StringComparer.OrdinalIgnoreCase).Count();
        var shorterWordCount = Math.Min(candContent.Length, origContent.Length);

        if ((double)overlapWords / shorterWordCount >= 0.5)
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

        // Density is meaningless for tiny JDs — a 2-word JD like "Looking for a
        // developer" produces a 0.5 density just from a legitimate "developer"
        // mention. Require a real JD before the heuristic runs.
        const int MinJdKeywords = 5;
        if (jdWords.Count < MinJdKeywords) return;

        var resumeText = SerializeResume(tailored);
        var resumeWords = resumeText.Split(new[] { ' ', '\n', '\r', '.' }, StringSplitOptions.RemoveEmptyEntries);

        if (resumeWords.Length == 0) return;

        var matchCount = jdWords.Count(jd => resumeWords.Any(r => r.Contains(jd, StringComparison.OrdinalIgnoreCase)));
        var density = (double)matchCount / jdWords.Count;

        // 30% threshold catches LLM-driven stuffing without flagging the natural
        // overlap a genuinely-tailored resume should have with the JD. Tuned
        // empirically against the templates in src/Envoy.Templates/.
        const double KeywordStuffingThreshold = 0.30;

        if (density > KeywordStuffingThreshold)
        {
            result.Violations.Add(new SafetyViolation
            {
                Type = "KeywordDensity",
                Description = $"Keyword density {density:P0} exceeds {KeywordStuffingThreshold:P0}. Risk of keyword stuffing.",
                Field = "Global"
            });
            _log.LogWarning("Keyword density {Density:P0} exceeds {Threshold:P0}", density, KeywordStuffingThreshold);
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

        var s = dateStr.Trim();

        // 'Present' / 'Current' aren't dates — let the IsCurrent flag handle them
        // upstream and report the field as un-parseable here.
        if (s.Equals("Present", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Current", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Now", StringComparison.OrdinalIgnoreCase))
            return false;

        if (DateTime.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;

        // Last-resort generic parse so we still catch fully-qualified
        // ISO-ish strings, but no more regex digit-stripping (that turned
        // "Jan 2024" into "122024" which then parsed as garbage).
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
