using Envoy.Core.Models;
using Envoy.Core.Services;
using Xunit;

namespace Envoy.Core.Tests;

public class TemplateEngineGateTests
{
    // Shared fake page: kept public so the run-mechanics suite (TemplateEngineRunTests)
    // can reuse the same offline interactor.
    public sealed class FakePageInteractor : IPageInteractor
    {
        public int ClickCount { get; private set; }
        public List<string> Typed { get; } = new();
        public List<string> ClickedSelectors { get; } = new();
        public string PageUrl { get; set; } = "https://example.com/apply";
        public string PageText { get; set; } = "";
        public bool MissingElement { get; set; }
        public string? FoundNode { get; set; } = "node-1";

        public Task<string?> QuerySelectorAsync(string selector, CancellationToken ct = default) =>
            Task.FromResult(MissingElement ? null : FoundNode);
        public Task<List<string>> QuerySelectorAllAsync(string selector, CancellationToken ct = default) =>
            Task.FromResult(MissingElement ? new List<string>() : new List<string> { "node-1" });
        public Task FocusAsync(string nodeId, CancellationToken ct = default) => Task.CompletedTask;
        public Task TypeTextAsync(string nodeId, string text, CancellationToken ct = default) { Typed.Add(text); return Task.CompletedTask; }
        public Task ClickAsync(string nodeId, CancellationToken ct = default) { ClickCount++; ClickedSelectors.Add(nodeId); return Task.CompletedTask; }
        public Task SetFileInputAsync(string nodeId, string filePath, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> GetPageTextAsync(CancellationToken ct = default) => Task.FromResult(PageText);
        public Task<string?> GetPageUrlAsync(CancellationToken ct = default) => Task.FromResult<string?>(PageUrl);
        public Task<byte[]> CaptureScreenshotAsync(CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task<bool> DetectCaptchaAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task NavigateAsync(string url, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static SiteTemplate SubmitTemplate(bool requireConfirmation) => new()
    {
        Id = "test",
        Name = "Test",
        UrlMatch = "example.com/*",
        Steps =
        {
            new TemplateStep
            {
                Action = "conditional_click",
                Description = "Submit application",
                Selector = "#submit",
                RequireConfirmation = requireConfirmation
            }
        }
    };

    // Point at an empty temp dir so no real templates load.
    private static TemplateEngine Engine() =>
        new(Path.Combine(Path.GetTempPath(), "envoy-no-templates-" + Guid.NewGuid()), elementLocator: null);

    [Fact]
    public async Task ConfirmationDeclined_DoesNotClickSubmit()
    {
        var browser = new FakePageInteractor();

        var result = await Engine().ExecuteTemplateAsync(
            SubmitTemplate(requireConfirmation: true), browser, new TailoredProfile(),
            _ => Task.FromResult(false)); // user declines

        Assert.Equal(0, browser.ClickCount);
        Assert.Null(result.VerifyPassed); // no submission → no verification claim
    }

    [Fact]
    public async Task ConfirmationApproved_ClicksSubmit()
    {
        var browser = new FakePageInteractor();

        var result = await Engine().ExecuteTemplateAsync(
            SubmitTemplate(requireConfirmation: true), browser, new TailoredProfile(),
            _ => Task.FromResult(true)); // user confirms

        Assert.Equal(1, browser.ClickCount);
        Assert.Equal(1, result.AttemptedCount);
        Assert.Equal(1, result.FilledCount);
    }

    [Fact]
    public async Task NoConfirmationRequired_ClicksWithoutPrompting()
    {
        var browser = new FakePageInteractor();
        var prompted = false;

        await Engine().ExecuteTemplateAsync(
            SubmitTemplate(requireConfirmation: false), browser, new TailoredProfile(),
            _ => { prompted = true; return Task.FromResult(true); });

        Assert.False(prompted);
        Assert.Equal(1, browser.ClickCount);
    }
}
