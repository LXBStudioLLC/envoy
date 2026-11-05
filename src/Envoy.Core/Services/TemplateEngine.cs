using Envoy.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Envoy.Core.Services;

public class Fingerprint
{
    public string? Tag { get; set; }
    public Dictionary<string, string>? Attributes { get; set; }
    [JsonPropertyName("label_text")]
    public string? LabelText { get; set; }
    [JsonPropertyName("text_content")]
    public string? TextContent { get; set; }
    [JsonPropertyName("ancestor_chain")]
    public List<string>? AncestorChain { get; set; }
    [JsonPropertyName("siblings_before")]
    public List<string>? SiblingsBefore { get; set; }
    [JsonPropertyName("siblings_after")]
    public List<string>? SiblingsAfter { get; set; }
    [JsonPropertyName("position_index")]
    public int? PositionIndex { get; set; }
}

public class TemplateStep
{
    public string Action { get; set; } = string.Empty;
    public string? Selector { get; set; }
    [JsonPropertyName("field_id")]
    public string? FieldId { get; set; }
    [JsonPropertyName("value_from")]
    public string? ValueFrom { get; set; }
    [JsonPropertyName("fallback_selector")]
    public string? FallbackSelector { get; set; }
    [JsonPropertyName("require_confirmation")]
    public bool RequireConfirmation { get; set; }

    /// <summary>
    /// APP-05: a required fill/upload that ends up skipped (element missing or
    /// no resolvable value) makes the whole run fail honestly instead of
    /// reporting a Completed application that sent nothing.
    /// </summary>
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    public int Timeout { get; set; } = 5000;
    public string? Description { get; set; }
    public Fingerprint? Fingerprint { get; set; }
}

/// <summary>
/// POST-05 (APP-14 will carry the evidence): after the submit click, the engine
/// can verify the application actually went through — a URL fragment, an
/// on-page text, or an element that only exists on the post-submission page.
/// Conditions are AND-ed; every condition absent stays unverified.
/// </summary>
public class TemplateVerify
{
    [JsonPropertyName("url_contains")]
    public string? UrlContains { get; set; }
    [JsonPropertyName("text_contains")]
    public string? TextContains { get; set; }
    [JsonPropertyName("selector_present")]
    public string? SelectorPresent { get; set; }
}

public class SiteTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("url_match")]
    public string UrlMatch { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public List<TemplateStep> Steps { get; set; } = new();
    [JsonPropertyName("verify")]
    public TemplateVerify? Verify { get; set; }
}

/// <summary>
/// Honest per-run accounting (APP-05): what the engine attempted, what it
/// actually filled, and whether the post-submit verify block passed. The
/// orchestrator turns required-missing skips into a Failed application and
/// verify into a Completed-with-verification evidence.
/// </summary>
public sealed class TemplateRunResult
{
    public int AttemptedCount { get; internal set; }
    public int FilledCount { get; internal set; }
    public int SkippedCount { get; internal set; }
    public int RequiredTotal { get; internal set; }
    public int RequiredMissing { get; internal set; }
    /// <summary>Total characters actually typed across fills (0 → suspicious).</summary>
    public int TypedCharacters { get; internal set; }
    public bool? VerifyPassed { get; internal set; }
    public string? VerifyNote { get; internal set; }
}

public class TemplateEngine
{
    private readonly List<SiteTemplate> _templates = new();
    private readonly string _templatesPath;
    private readonly IElementLocator? _elementLocator;
    private readonly ILogger<TemplateEngine>? _log;

    public TemplateEngine(string templatesPath, IElementLocator? elementLocator = null, ILogger<TemplateEngine>? logger = null)
    {
        _templatesPath = templatesPath;
        _elementLocator = elementLocator;
        _log = logger;
        LoadTemplates();
    }

    public SiteTemplate? MatchTemplate(string url)
    {
        return _templates.FirstOrDefault(t => UrlMatchesGlob(url, t.UrlMatch));
    }

    // Glob-to-regex matching anchored at the start of the URL so that the
    // pattern 'linkedin.com/jobs/*' matches https://www.linkedin.com/jobs/...
    // but NOT notlinkedin.com/jobs/... — the old substring approach matched
    // both. Templates may omit or include the http(s):// prefix; we strip
    // it before regex-escaping so both forms work.
    private static bool UrlMatchesGlob(string url, string glob)
    {
        if (string.IsNullOrWhiteSpace(glob)) return false;
        var normalizedGlob = Regex.Replace(glob, @"^https?://", "", RegexOptions.IgnoreCase);
        var escaped = Regex.Escape(normalizedGlob).Replace("\\*", ".*");
        var pattern = $"^(?:https?://)?(?:www\\.)?{escaped}$";
        return Regex.IsMatch(url, pattern, RegexOptions.IgnoreCase);
    }

    public async Task<TemplateRunResult> ExecuteTemplateAsync(
        SiteTemplate template,
        IPageInteractor browser,
        TailoredProfile profile,
        Func<string, Task<bool>> onConfirmationRequired,
        CancellationToken ct = default)
    {
        var result = new TemplateRunResult();

        foreach (var step in template.Steps)
        {
            ct.ThrowIfCancellationRequested();

            switch (step.Action.ToLowerInvariant())
            {
                case "wait_for":
                    await WaitForElementAsync(browser, step, ct);
                    break;

                case "click":
                    result.RequiredTotal += step.Required ? 1 : 0;
                    var clickNode = await FindElementAsync(browser, step, template.Id, ct);
                    result.AttemptedCount++;
                    if (clickNode != null)
                    {
                        await browser.ClickAsync(clickNode, ct);
                        result.FilledCount++;
                    }
                    else
                    {
                        result.SkippedCount++;
                        if (step.Required)
                            result.RequiredMissing++;
                        _log?.LogWarning("[{Template}] click step skipped: element not found ({Selector})", template.Id, step.Selector);
                    }
                    break;

                case "fill":
                    result.RequiredTotal += step.Required ? 1 : 0;
                    var fillNode = await FindElementAsync(browser, step, template.Id, ct);
                    result.AttemptedCount++;
                    if (fillNode == null)
                    {
                        result.SkippedCount++;
                        if (step.Required)
                            result.RequiredMissing++;
                        _log?.LogWarning("[{Template}] fill step skipped: element not found ({Selector})", template.Id, step.Selector);
                        break;
                    }

                    var value = GetValueFromProfile(profile, step.ValueFrom ?? step.FieldId ?? "");
                    if (string.IsNullOrEmpty(value))
                    {
                        result.SkippedCount++;
                        if (step.Required)
                            result.RequiredMissing++;
                        _log?.LogWarning(
                            "[{Template}] fill step skipped: no value for profile field '{Field}'",
                            template.Id, step.ValueFrom ?? step.FieldId);
                        break;
                    }

                    await browser.TypeTextAsync(fillNode, value, ct);
                    result.FilledCount++;
                    result.TypedCharacters += value.Length;
                    break;

                case "upload":
                    result.RequiredTotal += step.Required ? 1 : 0;
                    var uploadNode = await FindElementAsync(browser, step, template.Id, ct);
                    result.AttemptedCount++;
                    if (uploadNode == null)
                    {
                        result.SkippedCount++;
                        if (step.Required)
                            result.RequiredMissing++;
                        _log?.LogWarning("[{Template}] upload step skipped: file input not found ({Selector})", template.Id, step.Selector);
                        break;
                    }

                    var filePath = GetValueFromProfile(profile, step.ValueFrom ?? "");
                    if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                    {
                        result.SkippedCount++;
                        if (step.Required)
                            result.RequiredMissing++;
                        _log?.LogWarning("[{Template}] upload step skipped: file missing ({Path})", template.Id, filePath);
                        break;
                    }

                    await UploadFileAsync(browser, uploadNode, filePath, ct);
                    result.FilledCount++;
                    break;

                case "conditional_click":
                    if (step.RequireConfirmation)
                    {
                        // Human-gated submit: block on explicit approval. If the user
                        // declines, leave the filled form in place for manual review and
                        // stop — never click submit without consent.
                        var approved = await onConfirmationRequired(step.Description ?? "Submit application?");
                        if (!approved)
                        {
                            // Verification is meaningless without a submission; the
                            // declined run is recorded as-is (no verify).
                            return result;
                        }
                    }

                    var submitNode = await FindElementAsync(browser, step, template.Id, ct);
                    result.AttemptedCount++;
                    if (submitNode != null)
                    {
                        await browser.ClickAsync(submitNode, ct);
                        result.FilledCount++;

                        // APP-05: post-submit verification lives here because this
                        // is the only place that knows a submit click happened.
                        if (template.Verify != null)
                        {
                            var (passed, note) = await VerifySubmissionAsync(template.Verify, browser, ct);
                            result.VerifyPassed = passed;
                            result.VerifyNote = note;
                        }
                    }
                    else
                    {
                        result.SkippedCount++;
                        if (step.Required)
                            result.RequiredMissing++;
                        _log?.LogWarning("[{Template}] conditional_click skipped: element not found ({Selector})", template.Id, step.Selector);
                    }
                    break;
            }
        }

        _log?.LogInformation(
            "Template {Id} run: attempted {Attempted}, filled {Filled}, skipped {Skipped}, required-missing {Missing}, typed {Chars} chars, verify {Verify}",
            template.Id, result.AttemptedCount, result.FilledCount, result.SkippedCount, result.RequiredMissing,
            result.TypedCharacters,
            result.VerifyPassed is null ? "n/a" : result.VerifyPassed.Value ? "passed" : "failed");

        return result;
    }

    private async Task<(bool Passed, string Note)> VerifySubmissionAsync(TemplateVerify verify, IPageInteractor browser, CancellationToken ct)
    {
        try
        {
            var checks = new List<string>();

            if (!string.IsNullOrWhiteSpace(verify.UrlContains))
            {
                var url = await browser.GetPageUrlAsync(ct) ?? string.Empty;
                var ok = url.Contains(verify.UrlContains, StringComparison.OrdinalIgnoreCase);
                checks.Add(ok ? $"url contains '{verify.UrlContains}'" : $"url no longer contains '{verify.UrlContains}'");
                if (!ok)
                    return (false, "submission could not be verified: " + string.Join("; ", checks));
            }

            if (!string.IsNullOrWhiteSpace(verify.TextContains))
            {
                var text = await browser.GetPageTextAsync(ct);
                var ok = text.Contains(verify.TextContains, StringComparison.OrdinalIgnoreCase);
                checks.Add(ok ? $"page shows '{verify.TextContains}'" : $"'{verify.TextContains}' not found on the page");
                if (!ok)
                    return (false, "submission could not be verified: " + string.Join("; ", checks));
            }

            if (!string.IsNullOrWhiteSpace(verify.SelectorPresent))
            {
                var node = await browser.QuerySelectorAsync(verify.SelectorPresent, ct);
                var ok = node != null;
                checks.Add(ok ? $"element '{verify.SelectorPresent}' present" : $"element '{verify.SelectorPresent}' absent");
                if (!ok)
                    return (false, "submission could not be verified: " + string.Join("; ", checks));
            }

            return (true, "verified: " + string.Join("; ", checks));
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Submission verification failed");
            return (false, "verification could not run: " + ex.Message);
        }
    }

    private async Task WaitForElementAsync(IPageInteractor browser, TemplateStep step, CancellationToken ct)
    {
        var timeout = TimeSpan.FromMilliseconds(step.Timeout);
        var start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < timeout)
        {
            var node = await browser.QuerySelectorAsync(step.Selector ?? "", ct);
            if (node != null) return;
            await Task.Delay(500, ct);
        }
    }

    private async Task<string?> FindElementAsync(IPageInteractor browser, TemplateStep step, string templateId, CancellationToken ct)
    {
        if (_elementLocator != null)
        {
            var result = await _elementLocator.LocateAsync(step, templateId, ct);
            return result.NodeId;
        }

        if (!string.IsNullOrEmpty(step.Selector))
        {
            var node = await browser.QuerySelectorAsync(step.Selector, ct);
            if (node != null) return node;
        }

        if (!string.IsNullOrEmpty(step.FallbackSelector))
        {
            var node = await browser.QuerySelectorAsync(step.FallbackSelector, ct);
            if (node != null) return node;
        }

        return null;
    }

    private string GetValueFromProfile(TailoredProfile profile, string field)
    {
        var data = profile.TailoredData;
        var name = data.Name ?? "";
        var nameParts = name.Split(' ', 2);
        var key = (field ?? "").ToLowerInvariant();

        switch (key)
        {
            case "name": return name;
            case "first_name": return nameParts.Length > 0 ? nameParts[0] : name;
            case "last_name": return nameParts.Length > 1 ? nameParts[1] : "";
            case "email": return data.Email ?? "";
            case "contact_email": return data.Email ?? "";
            case "phone": return data.Phone ?? "";
            case "location": return data.Location ?? "";
            case "linkedin": return data.LinkedIn ?? "";
            case "linkedin_url": return data.LinkedIn ?? "";
            case "website": return data.Website ?? "";
            case "portfolio_url": return data.Website ?? "";
            case "summary": return data.Summary ?? "";
            case "org":
            case "company":
            case "current_company": return data.Experience.FirstOrDefault()?.Company ?? "";
            case "current_title": return data.Experience.FirstOrDefault()?.JobTitle ?? "";
            case "years_experience": return FormProfileCatalog.Resolve("years_experience", data) ?? "";
            case "city": return ParseLocation(data.Location, "city") ?? "";
            case "state": return ParseLocation(data.Location, "state") ?? "";
            case "zip": return ""; // no postal-code data in the model yet; falls to the human
            case "cover_letter": return profile.CoverLetterText ?? "";
            case "resume_file":
            case "generated_pdf_path":
                // Same sanitized path the PDF was written to, so the upload always finds
                // it and untrusted Company/JobTitle can't inject a traversal.
                return ResumeFilePath.For(name, profile.Company, profile.JobTitle);
        }

        // Unknown field — a real ILogger line so Release builds are debuggable
        // (Debug.WriteLine never reached a user or a log file), and an empty
        // value MUST not be typed into a live form.
        _log?.LogWarning(
            "Unknown profile field '{Field}' requested by template; the fill is skipped and counted as missing",
            field);
        return "";
    }

    /// <summary>Splits classic "City, ST" location strings (APP-10, documented field ids).</summary>
    internal static string? ParseLocation(string? location, string want)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        var parts = location.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        if (parts.Length >= 2)
            return want == "city" ? parts[0] : parts[^1];
        return want == "city" ? parts[0] : null;
    }

    private static async Task UploadFileAsync(IPageInteractor browser, string nodeId, string filePath, CancellationToken ct)
    {
        await browser.SetFileInputAsync(nodeId, filePath, ct);
    }

    private static readonly JsonSerializerOptions TemplateJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private void LoadTemplates()
    {
        if (!Directory.Exists(_templatesPath))
            return;

        foreach (var file in Directory.GetFiles(_templatesPath, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var template = JsonSerializer.Deserialize<SiteTemplate>(json, TemplateJsonOptions);
                if (template != null)
                    _templates.Add(template);
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "Failed to load template {File}", file);
            }
        }
    }
}
