using Envoy.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Envoy.Core.Services;

/// <summary>
/// Prompt construction for the field mapper (APP-09), plus the strict profile
/// catalog the plan may reference. Prompts and validator share the catalog so
/// they cannot drift.
/// </summary>
public static class FormFieldPrompts
{
    /// <summary>The only profile keys a plan may reference as "pf:{key}".</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Catalog = new List<(string, string)>
    {
        ("email", "Email address"),
        ("first_name", "First name"),
        ("last_name", "Last name"),
        ("full_name", "Full name"),
        ("phone", "Phone number"),
        ("location", "City / state, as on the resume"),
        ("linkedin", "LinkedIn profile URL"),
        ("website", "Personal website / portfolio URL"),
        ("summary", "Professional summary paragraph (for 'tell us about yourself' boxes)"),
        ("current_company", "Most recent employer"),
        ("current_title", "Most recent job title"),
        ("years_experience", "Years of professional experience (number)"),
        ("skills", "Skills, comma-separated (for skills keyword boxes)")
    };

    /// <summary>Verify the catalog contains a key; true/false used by toggle plans bypasses this.</summary>
    public static bool IsKnownKey(string key) =>
        Catalog.Any(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)) ||
        key is "resume_file"; // the mapper treats upload separately; the actuator resolves the pdf path

    /// <summary>
    /// Heuristic: label/type look like a question that may carry a stored
    /// answer (text/comment/why/how/describe boxes) — small so the mapper's
    /// candidate list stays short.
    /// </summary>
    public static bool QuestionLikely(string? label, string type)
    {
        if (FormFieldInfo.FillableTypes.Contains(type) is false)
            return false;

        var l = (label ?? "").ToLowerInvariant();
        if (l.Length < 4) return false;

        return l.Contains("tell us") ||
               l.Contains("describe") ||
               l.Contains("why ") ||
               l.Contains("how ") ||
               l.Contains("what makes") ||
               l.Contains("additional information") ||
               l.Contains("cover letter") ||
               l.Contains("questions") ||
               l.Contains("comments") ||
               l.Contains("about yourself");
    }

    public static (string Prompt, string System) BuildPrompt(
        IReadOnlyList<FormFieldInfo> fields,
        MasterProfile profile,
        IReadOnlyList<(int Slot, int BankId, AnswerBankEntry Entry, string LiveLabel)> candidates)
    {
        var system =
            "You are a careful job-application form filler. You map form fields to the user's own data. " +
            "CRITICAL RULES:\n" +
            "1. NEVER invent values. Value sources must be exactly 'pf:<key>' (profile fields listed below) or '#<slot>' (stored answers provided below).\n" +
            "2. For one-off questions with no stored answer and no profile field, return action 'needs_human'.\n" +
            "3. Salary expectations, work authorization, visa/sponsorship, race/gender/veteran/disability (EEO), references: ALWAYS 'needs_human' unless a stored answer directly answers that exact question.\n" +
            "4. 'select' value_source must be the EXACT option text from the field's options list.\n" +
            "5. 'toggle' value_source must be 'pf:true' or 'pf:false'.\n" +
            "6. Skip fields that are already visibly pre-filled with the user's data.\n" +
            "7. Output strict JSON: {\"fields\":[{\"index\":N,\"action\":\"fill|select|toggle|upload|skip|needs_human\",\"value_source\":\"...\",\"note\":\"short reason\"}]}";

        var fieldsJson = JsonSerializer.Serialize(fields.Select(f => new
        {
            index = f.Index,
            type = f.Type,
            label = f.Label,
            required = f.Required,
            options = f.Options.Count > 0 ? f.Options : null,
            current_value = f.CurrentValue
        }), new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        var profileJson = JsonSerializer.Serialize(FormProfileCatalog.Resolve(profile), new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        var bankJson = JsonSerializer.Serialize(
            candidates.Select(c => new { slot = c.Slot, for_label = c.LiveLabel, answer = c.Entry.Answer }),
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        var prompt =
            "FORM FIELDS (enumerated from the page):\n" + fieldsJson + "\n\n" +
            "PROFILE FIELDS (usable as pf:<key>):\n" + profileJson + "\n\n" +
            "STORED ANSWERS (usable as #<slot>, for the labeled questions):\n" + bankJson + "\n\n" +
            "Map every listed field. Return only JSON.";

        return (prompt, system);
    }

    /// <summary>Cover-letter prompt: role, the JD's asks, the resume facts, and the tailored bullets (APP-10).</summary>
    public static string BuildCoverLetterPrompt(string? jobTitle, string? company, string jobDescription, MasterProfile master, MasterProfile tailored)
    {
        var facts = new System.Text.StringBuilder();
        facts.AppendLine($"Name: {master.Name}");
        var latest = tailored.Experience.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Company));
        facts.AppendLine($"Most recent role: {latest?.JobTitle ?? "n/a"} at {latest?.Company ?? "n/a"}");
        facts.AppendLine($"Skills: {string.Join(", ", master.Skills)}");
        foreach (var exp in tailored.Experience.Where(e => !string.IsNullOrWhiteSpace(e.Company)).Take(3))
        {
            facts.AppendLine($"At {exp.Company} ({exp.StartDate}–{exp.EndDate ?? "present"}):");
            foreach (var bullet in exp.Bullets.Take(4))
                facts.AppendLine($"  - {bullet}");
        }

        var trimmedJd = jobDescription?.Trim() ?? string.Empty;
        if (trimmedJd.Length > 2500)
            trimmedJd = trimmedJd.Substring(0, 2500);

        return $"""
            Write a cover letter for the {jobTitle ?? "this"} role at {company ?? "this company"}.

            Job description (trimmed):
            {trimmedJd}

            Only these facts may appear:
            {facts}

            Structure: short opening tying the role to the experience; one paragraph with the strongest matching work; short close. Plain text.
            """;
    }
}

/// <summary>
/// Resolves "pf:{key}" values against the real profile. The single place that
/// knows how profile data becomes generic-form answers.
/// </summary>
public static class FormProfileCatalog
{
    public static IReadOnlyList<(string Key, string Label)> PublicCatalog => FormFieldPrompts.Catalog;

    public static bool IsKnownKey(string key) => FormFieldPrompts.IsKnownKey(key);

    /// <summary>Resolve a plan value source to the actual string to type/choose.</summary>
    public static string? Resolve(string key, MasterProfile profile)
    {
        var data = profile;
        var name = data.Name ?? "";
        var firstName = name.Split(' ', 2)[0];
        var lastName = name.Split(' ', 2).Length > 1 ? name.Split(' ', 2)[1] : $"";
        var latest = data.Experience
            .OrderByDescending(e => ParseStart(e.StartDate))
            .FirstOrDefault();

        return key.ToLowerInvariant() switch
        {
            "email" => data.Email,
            "first_name" => firstName,
            "last_name" => lastName,
            "full_name" => name,
            "phone" => data.Phone,
            "location" => data.Location,
            "linkedin" => data.LinkedIn,
            "website" => data.Website,
            "summary" => data.Summary,
            "current_company" => latest?.Company,
            "current_title" => latest?.JobTitle,
            "years_experience" => YearsExperience(data.Experience).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
            "skills" => data.Skills is { Count: > 0 } ? string.Join(", ", data.Skills) : null,
            "true" => "true",
            "false" => "false",
            _ => null
        };

        static DateTime ParseStart(string? date)
        {
            if (DateTime.TryParse(date, out var d)) return d;
            // "2021-04" and bare years parse as floats here by TryParse on DateTime — good enough for ordering.
            return DateTime.MinValue;
        }
    }

    /// <summary>All catalog keys with concrete values, for the mapper prompt.</summary>
    public static Dictionary<string, string?> Resolve(MasterProfile profile)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, _) in FormFieldPrompts.Catalog)
            result[key] = Resolve(key, profile);
        return result;
    }

    internal static double YearsExperience(IEnumerable<ExperienceEntry>? experience)
    {
        if (experience is null || !experience.Any()) return 0;

        var spans = new List<(DateTime Start, DateTime End)>();
        foreach (var e in experience)
        {
            var start = TryParse(e.StartDate);
            if (start is null) continue;
            var end = e.IsCurrent ? DateTime.UtcNow : (TryParse(e.EndDate) ?? DateTime.UtcNow);
            spans.Add((start.Value, end));
        }
        if (spans.Count == 0) return 0;

        // Merge overlapping ranges so double-counting jobs doesn't inflate.
        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        double years = 0;
        var cursor = spans[0].Start;
        foreach (var (start, end) in spans)
        {
            var from = start > cursor ? start : cursor;
            if (from < end) years += (end - from).TotalDays / 365.25;
            if (end > cursor) cursor = end;
        }
        return Math.Max(0, years);

        static DateTime? TryParse(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (s.Length == 4 && s.All(char.IsDigit)) return new DateTime(int.Parse(s), 1, 1);
            if (DateTime.TryParse(s, out var d)) return d;
            return null;
        }
    }
}
