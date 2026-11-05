using Envoy.Core.Models;
using Envoy.Core.Services;
using System.Text.Json;
using Xunit;

namespace Envoy.Core.Tests;

/// <summary>
/// APP-05: honest per-step accounting for template runs. The engine must
/// report what it attempted, what it actually filled, and never silently
/// fill nothing while claiming success. All offline via the fake page
/// interactor; no browser, no network.
/// </summary>
public sealed class TemplateEngineRunTests
{
    private static TemplateStep Fill(string fieldId, string selector, bool required = true) => new()
    {
        Action = "fill", FieldId = fieldId, Selector = selector, Required = required
    };

    private static TailoredProfile Profile(int? overrideName = null) => new()
    {
        Company = "Acme, Inc.",
        JobTitle = "Software Engineer",
        TailoredData = new MasterProfile
        {
            Name = overrideName is null ? "Mary Ann Smith" : "Mary",
            Email = "mary@example.com",
            Phone = "+1 555 0100"
        }
    };

    private static TemplateEngine Engine() =>
        new(Path.Combine(Path.GetTempPath(), "envoy-no-templates-" + Guid.NewGuid()), elementLocator: null);

    [Fact]
    public async Task AllRequiredFieldsResolved_CountsClean()
    {
        var browser = new TemplateEngineGateTests.FakePageInteractor();
        var template = new SiteTemplate
        {
            Id = "t", Name = "T", UrlMatch = "example.com/*",
            Steps = { Fill("first_name", "#first"), Fill("last_name", "#last"), Fill("email", "#email") }
        };

        var result = await Engine().ExecuteTemplateAsync(template, browser, Profile(), _ => Task.FromResult(true));

        Assert.Equal(3, result.AttemptedCount);
        Assert.Equal(3, result.FilledCount);
        Assert.Equal(0, result.RequiredMissing);
        Assert.Equal(3, result.RequiredTotal);
        Assert.Equal(len("Mary") + len("Ann Smith") + len("mary@example.com"), result.TypedCharacters);
        // "Mary Ann Smith" splits first/last on the FIRST space: last_name is
        // "Ann Smith", not "Smith" — existing behavior, now observable.
    }

    [Fact]
    public async Task MissingElement_OnRequired_ReportsMissing()
    {
        var browser = new TemplateEngineGateTests.FakePageInteractor { MissingElement = true };
        var template = new SiteTemplate
        {
            Id = "t", Name = "T", UrlMatch = "example.com/*",
            Steps = { Fill("email", "#email", required: true) }
        };

        var result = await Engine().ExecuteTemplateAsync(template, browser, Profile(), _ => Task.FromResult(true));

        Assert.Equal(1, result.RequiredMissing);
        Assert.Equal(0, result.FilledCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Empty(browser.Typed); // nothing typed into the live page
    }

    [Fact]
    public async Task MissingElement_OnOptional_DoesNotFailIntegrity()
    {
        var browser = new TemplateEngineGateTests.FakePageInteractor { MissingElement = true };
        var template = new SiteTemplate
        {
            Id = "t", Name = "T", UrlMatch = "example.com/*",
            Steps = { Fill("summary", "#summary", required: false) }
        };

        var result = await Engine().ExecuteTemplateAsync(template, browser, Profile(), _ => Task.FromResult(true));

        Assert.Equal(0, result.RequiredMissing);
        Assert.Equal(1, result.SkippedCount);
    }

    [Fact]
    public async Task UnknownProfileField_SkipsNotFills()
    {
        // The documented-but-unmapped field list (address, zip, cover_letter…)
        // MUST never produce an empty-string fill on a live form.
        var browser = new TemplateEngineGateTests.FakePageInteractor();
        var template = new SiteTemplate
        {
            Id = "t", Name = "T", UrlMatch = "example.com/*",
            Steps = { Fill("cover_letter", "#cover") }
        };

        var result = await Engine().ExecuteTemplateAsync(template, browser, Profile(), _ => Task.FromResult(true));

        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(0, result.FilledCount);
        Assert.Equal(1, result.RequiredMissing);
        Assert.Empty(browser.Typed);
    }

    [Fact]
    public async Task Verify_UrlContains_Passes()
    {
        var browser = new TemplateEngineGateTests.FakePageInteractor
        {
            PageUrl = "https://example.com/application_confirmation?ok=1"
        };
        var template = new SiteTemplate
        {
            Id = "t", Name = "T", UrlMatch = "example.com/*",
            Verify = new TemplateVerify { UrlContains = "confirmation" },
            Steps = { new TemplateStep { Action = "conditional_click", Selector = "#submit", RequireConfirmation = true, Description = "Send" } }
        };

        var result = await Engine().ExecuteTemplateAsync(template, browser, Profile(), _ => Task.FromResult(true));

        Assert.True(result.VerifyPassed);
        Assert.Contains("url contains", result.VerifyNote);
    }

    [Fact]
    public async Task Verify_TextMissing_FailsHonest()
    {
        var browser = new TemplateEngineGateTests.FakePageInteractor { PageText = "Thanks! We got it." };
        var template = new SiteTemplate
        {
            Id = "t", Name = "T", UrlMatch = "example.com/*",
            Verify = new TemplateVerify { TextContains = "application received" },
            Steps = { new TemplateStep { Action = "conditional_click", Selector = "#submit", RequireConfirmation = true, Description = "Send" } }
        };

        var result = await Engine().ExecuteTemplateAsync(template, browser, Profile(), _ => Task.FromResult(true));

        Assert.False(result.VerifyPassed);
        Assert.Contains("could not be verified", result.VerifyNote);
        Assert.Equal(1, browser.ClickCount); // the submission still went out — only the VERIFICATION is honest about it
    }

    [Fact]
    public async Task DeclineGate_SkipsVerificationEntirely()
    {
        var browser = new TemplateEngineGateTests.FakePageInteractor();
        var template = new SiteTemplate
        {
            Id = "t", Name = "T", UrlMatch = "example.com/*",
            Verify = new TemplateVerify { UrlContains = "confirmation" },
            Steps = { new TemplateStep { Action = "conditional_click", Selector = "#submit", RequireConfirmation = true, Description = "Send" } }
        };

        var result = await Engine().ExecuteTemplateAsync(template, browser, Profile(), _ => Task.FromResult(false));

        Assert.Equal(0, browser.ClickCount);
        Assert.Null(result.VerifyPassed);
    }

    [Fact]
    public void TemplateJson_RequiredAndVerify_RoundTrip()
    {
        var json = """
        {
          "id": "x", "name": "X", "url_match": "x.com/*",
          "steps": [
            { "action": "fill", "field_id": "email", "selector": "#e", "required": true },
            { "action": "fill", "field_id": "summary", "selector": "#s" }
          ],
          "verify": { "url_contains": "ok", "text_contains": "thanks", "selector_present": ".done" }
        }
        """;

        var template = JsonSerializer.Deserialize<SiteTemplate>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.True(template.Steps[0].Required);
        Assert.False(template.Steps[1].Required);
        Assert.Equal("ok", template.Verify!.UrlContains);
        Assert.Equal("thanks", template.Verify.TextContains);
        Assert.Equal(".done", template.Verify.SelectorPresent);
    }

    private static int len(string s) => s.Length;
}
