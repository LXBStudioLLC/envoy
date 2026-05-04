using Envoy.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Envoy.Core.Services;

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

    public TemplateEngine(string templatesPath)
    {
        _templatesPath = templatesPath;
        LoadTemplates();
    }

    public SiteTemplate? MatchTemplate(string url)
    {
        return _templates.FirstOrDefault(t =>
            url.Contains(t.UrlMatch.Replace("*", ""), StringComparison.OrdinalIgnoreCase));
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
                    var clickNode = await FindElementAsync(browser, step, ct);
                    if (clickNode != null)
                        await browser.ClickAsync(clickNode, ct);
                    break;

                case "fill":
                    var fillNode = await FindElementAsync(browser, step, ct);
                    if (fillNode != null)
                    {
                        var value = GetValueFromProfile(profile, step.ValueFrom ?? step.FieldId ?? "");
                        if (!string.IsNullOrEmpty(value))
                            await browser.TypeTextAsync(fillNode, value, ct);
                    }
                    break;

                case "upload":
                    var uploadNode = await FindElementAsync(browser, step, ct);
                    if (uploadNode != null)
                    {
                        var filePath = GetValueFromProfile(profile, step.ValueFrom ?? "");
                        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                        {
                            // CDP file upload requires DOM.setFileInputFiles
                            await UploadFileAsync(browser, uploadNode, filePath, ct);
                        }
                    }
                    break;

                case "conditional_click":
                    if (step.RequireConfirmation)
                    {
                        await onConfirmationRequired(step.Description ?? "Submit application?");
                    }
                    var submitNode = await FindElementAsync(browser, step, ct);
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

    private async Task<string?> FindElementAsync(CdpBrowserService browser, TemplateStep step, CancellationToken ct)
    {
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
        var nameParts = (data.Name ?? "").Split(' ', 2);

        return (field ?? "").ToLower() switch
        {
            "name" => data.Name,
            "first_name" => nameParts.Length > 0 ? nameParts[0] : data.Name,
            "last_name" => nameParts.Length > 1 ? nameParts[1] : "",
            "email" => data.Email,
            "contact_email" => data.Email,
            "phone" => data.Phone,
            "location" => data.Location ?? "",
            "linkedin" => data.LinkedIn ?? "",
            "website" => data.Website ?? "",
            "summary" => data.Summary ?? "",
            "org" => data.Experience.FirstOrDefault()?.Company ?? "",
            "company" => data.Experience.FirstOrDefault()?.Company ?? "",
            "resume_file" or "generated_pdf_path" => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Envoy",
                $"{data.Name.Replace(" ", "_")}_{profile.Company}_{profile.JobTitle}.pdf"),
            _ => ""
        };
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
