using Envoy.Core.Models;
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
    public int Timeout { get; set; } = 5000;
    public string? Description { get; set; }
    public Fingerprint? Fingerprint { get; set; }
}

public class SiteTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("url_match")]
    public string UrlMatch { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public List<TemplateStep> Steps { get; set; } = new();
}

public class TemplateEngine
{
    private readonly List<SiteTemplate> _templates = new();
    private readonly string _templatesPath;
    private readonly IElementLocator? _elementLocator;

    public TemplateEngine(string templatesPath, IElementLocator? elementLocator = null)
    {
        _templatesPath = templatesPath;
        _elementLocator = elementLocator;
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

    public async Task ExecuteTemplateAsync(
        SiteTemplate template,
        CdpBrowserService browser,
        TailoredProfile profile,
        Func<string, Task> onConfirmationRequired,
        CancellationToken ct = default)
    {
        foreach (var step in template.Steps)
        {
            ct.ThrowIfCancellationRequested();

            switch (step.Action.ToLower())
            {
                case "wait_for":
                    await WaitForElementAsync(browser, step, ct);
                    break;

                case "click":
                    var clickNode = await FindElementAsync(browser, step, template.Id, ct);
                    if (clickNode != null)
                        await browser.ClickAsync(clickNode, ct);
                    break;

                case "fill":
                    var fillNode = await FindElementAsync(browser, step, template.Id, ct);
                    if (fillNode != null)
                    {
                        var value = GetValueFromProfile(profile, step.ValueFrom ?? step.FieldId ?? "");
                        if (!string.IsNullOrEmpty(value))
                            await browser.TypeTextAsync(fillNode, value, ct);
                    }
                    break;

                case "upload":
                    var uploadNode = await FindElementAsync(browser, step, template.Id, ct);
                    if (uploadNode != null)
                    {
                        var filePath = GetValueFromProfile(profile, step.ValueFrom ?? "");
                        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                        {
                            await UploadFileAsync(browser, uploadNode, filePath, ct);
                        }
                    }
                    break;

                case "conditional_click":
                    if (step.RequireConfirmation)
                    {
                        await onConfirmationRequired(step.Description ?? "Submit application?");
                    }
                    var submitNode = await FindElementAsync(browser, step, template.Id, ct);
                    if (submitNode != null)
                        await browser.ClickAsync(submitNode, ct);
                    break;
            }
        }
    }

    private async Task WaitForElementAsync(CdpBrowserService browser, TemplateStep step, CancellationToken ct)
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

    private async Task<string?> FindElementAsync(CdpBrowserService browser, TemplateStep step, string templateId, CancellationToken ct)
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

    private static string GetValueFromProfile(TailoredProfile profile, string field)
    {
        var data = profile.TailoredData;
        var name = data.Name ?? "";
        var nameParts = name.Split(' ', 2);
        var key = (field ?? "").ToLower();

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
            case "website": return data.Website ?? "";
            case "summary": return data.Summary ?? "";
            case "org":
            case "company": return data.Experience.FirstOrDefault()?.Company ?? "";
            case "resume_file":
            case "generated_pdf_path":
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Envoy",
                    $"{name.Replace(" ", "_")}_{profile.Company}_{profile.JobTitle}.pdf");
        }

        // Unknown field — log so template authors can debug typos rather than
        // silently filling the form with an empty string.
        System.Diagnostics.Debug.WriteLine(
            $"[TemplateEngine] Unknown profile field '{field}' requested by template; using empty string.");
        return "";
    }

    private static async Task UploadFileAsync(CdpBrowserService browser, string nodeId, string filePath, CancellationToken ct)
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
                System.Diagnostics.Debug.WriteLine($"Failed to load template {file}: {ex.Message}");
            }
        }
    }
}
