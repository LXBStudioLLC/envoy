using Envoy.Core.Configuration;
using Envoy.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Envoy.Core.Tests;

public class ElementLocatorTests
{
    private readonly Mock<IBrowserQuery> _mockBrowser;
    private readonly EnvoySettings _settings;
    private readonly RelocationLogger _relocationLogger;
    private readonly Mock<ILogger<ElementLocatorService>> _mockLog;

    public ElementLocatorTests()
    {
        _mockBrowser = new Mock<IBrowserQuery>();
        _settings = new EnvoySettings { RelocationConfidenceThreshold = 0.75 };
        _relocationLogger = new RelocationLogger();
        _mockLog = new Mock<ILogger<ElementLocatorService>>();
    }

    private ElementLocatorService CreateLocator()
    {
        return new ElementLocatorService(_mockBrowser.Object, _settings, _relocationLogger, _mockLog.Object);
    }

    private static TemplateStep MakeStep(
        string action = "fill",
        string? selector = null,
        string? fallbackSelector = null,
        string? fieldId = null,
        Fingerprint? fingerprint = null)
    {
        return new TemplateStep
        {
            Action = action,
            Selector = selector,
            FallbackSelector = fallbackSelector,
            FieldId = fieldId,
            Fingerprint = fingerprint
        };
    }

    private static Fingerprint MakeFingerprint(
        string? tag = null,
        Dictionary<string, string>? attributes = null,
        string? labelText = null,
        List<string>? ancestorChain = null,
        List<string>? siblingsBefore = null,
        List<string>? siblingsAfter = null,
        int? positionIndex = null)
    {
        return new Fingerprint
        {
            Tag = tag,
            Attributes = attributes,
            LabelText = labelText,
            AncestorChain = ancestorChain,
            SiblingsBefore = siblingsBefore,
            SiblingsAfter = siblingsAfter,
            PositionIndex = positionIndex
        };
    }

    [Fact]
    public async Task LocateAsync_SelectorHit_ReturnsNodeId_NoRelocation()
    {
        _mockBrowser.Setup(b => b.QuerySelectorAsync("#email", default))
            .ReturnsAsync("42");

        var step = MakeStep(selector: "#email", fingerprint: MakeFingerprint(tag: "input"));
        var locator = CreateLocator();

        var result = await locator.LocateAsync(step, "test-template");

        Assert.Equal("42", result.NodeId);
        Assert.Equal("#email", result.ResolvedSelector);
        Assert.Equal(1.0, result.Confidence);
        Assert.False(result.DidRelocate);
        Assert.Equal("#email", result.OriginalSelector);
        Assert.Null(result.FailureReason);

        _mockBrowser.Verify(b => b.EvaluateJsAsync(It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task LocateAsync_SelectorMiss_FallbackHit_ReturnsNodeId_NoRelocation()
    {
        _mockBrowser.Setup(b => b.QuerySelectorAsync("#email", default))
            .ReturnsAsync((string?)null);
        _mockBrowser.Setup(b => b.QuerySelectorAsync("input[type='email']", default))
            .ReturnsAsync("55");

        var step = MakeStep(selector: "#email", fallbackSelector: "input[type='email']");
        var locator = CreateLocator();

        var result = await locator.LocateAsync(step, "test-template");

        Assert.Equal("55", result.NodeId);
        Assert.Equal("input[type='email']", result.ResolvedSelector);
        Assert.False(result.DidRelocate);
    }

    [Fact]
    public async Task LocateAsync_SelectorMiss_HighConfidenceFingerprint_DidRelocate()
    {
        _mockBrowser.Setup(b => b.QuerySelectorAsync("#old-email", default))
            .ReturnsAsync((string?)null);
        _mockBrowser.Setup(b => b.EvaluateJsAsync(It.IsAny<string>(), default))
            .ReturnsAsync("[{\"tag\":\"input\",\"attributes\":{\"type\":\"email\",\"name\":\"email\"},\"textContent\":\"\",\"labelText\":\"Email Address\",\"ancestors\":[\"form#app\",\"div.field\"],\"siblingsBefore\":[\"First Name\"],\"siblingsAfter\":[\"Phone\"],\"positionIndex\":2,\"cssSelector\":\"input#new-email\"}]");
        _mockBrowser.Setup(b => b.QuerySelectorAsync("input#new-email", default))
            .ReturnsAsync("88");

        var step = MakeStep(
            selector: "#old-email",
            fingerprint: MakeFingerprint(
                tag: "input",
                attributes: new Dictionary<string, string> { ["type"] = "email", ["name"] = "email" },
                labelText: "Email Address",
                ancestorChain: new List<string> { "form#app", "div.field" },
                siblingsBefore: new List<string> { "First Name" },
                siblingsAfter: new List<string> { "Phone" },
                positionIndex: 2));

        var locator = CreateLocator();

        var result = await locator.LocateAsync(step, "test-template");

        Assert.Equal("88", result.NodeId);
        Assert.Equal("input#new-email", result.ResolvedSelector);
        Assert.True(result.DidRelocate);
        Assert.Equal("#old-email", result.OriginalSelector);
        Assert.True(result.Confidence >= 0.75);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async Task LocateAsync_SelectorMiss_LowConfidenceMatch_ReturnsNullLowConfidence()
    {
        _mockBrowser.Setup(b => b.QuerySelectorAsync("#old-email", default))
            .ReturnsAsync((string?)null);
        _mockBrowser.Setup(b => b.EvaluateJsAsync(It.IsAny<string>(), default))
            .ReturnsAsync("[{\"tag\":\"input\",\"attributes\":{\"type\":\"checkbox\",\"name\":\"remember\"},\"textContent\":\"\",\"labelText\":\"Remember me\",\"ancestors\":[\"form#other\"],\"siblingsBefore\":[],\"siblingsAfter\":[],\"positionIndex\":0,\"cssSelector\":\"input#remember\"}]");

        var step = MakeStep(
            selector: "#old-email",
            fingerprint: MakeFingerprint(
                tag: "input",
                attributes: new Dictionary<string, string> { ["type"] = "email", ["name"] = "email" },
                labelText: "Email Address"));

        var locator = CreateLocator();

        var result = await locator.LocateAsync(step, "test-template");

        Assert.Null(result.NodeId);
        Assert.Null(result.ResolvedSelector);
        Assert.Equal("low_confidence", result.FailureReason);
        Assert.False(result.DidRelocate);
    }

    [Fact]
    public async Task LocateAsync_SelectorMiss_NoFingerprint_ReturnsNullNoFingerprint()
    {
        _mockBrowser.Setup(b => b.QuerySelectorAsync("#missing", default))
            .ReturnsAsync((string?)null);

        var step = MakeStep(selector: "#missing");
        var locator = CreateLocator();

        var result = await locator.LocateAsync(step, "test-template");

        Assert.Null(result.NodeId);
        Assert.Null(result.ResolvedSelector);
        Assert.Equal("no_fingerprint", result.FailureReason);
        Assert.False(result.DidRelocate);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public async Task LocateAsync_SelectorMiss_BelowThreshold_ReturnsLowConfidence()
    {
        _settings.RelocationConfidenceThreshold = 0.95;

        _mockBrowser.Setup(b => b.QuerySelectorAsync("#old-email", default))
            .ReturnsAsync((string?)null);
        _mockBrowser.Setup(b => b.EvaluateJsAsync(It.IsAny<string>(), default))
            .ReturnsAsync("[{\"tag\":\"input\",\"attributes\":{\"type\":\"email\"},\"textContent\":\"\",\"labelText\":\"Email\",\"ancestors\":[],\"siblingsBefore\":[],\"siblingsAfter\":[],\"positionIndex\":0,\"cssSelector\":\"input#vague\"}]");

        var step = MakeStep(
            selector: "#old-email",
            fingerprint: MakeFingerprint(
                tag: "input",
                attributes: new Dictionary<string, string> { ["type"] = "email", ["name"] = "email" },
                labelText: "Email Address"));

        var locator = CreateLocator();

        var result = await locator.LocateAsync(step, "test-template");

        Assert.Null(result.NodeId);
        Assert.Null(result.ResolvedSelector);
        Assert.False(result.DidRelocate);
    }

    [Fact]
    public async Task LocateAsync_SelectorMiss_EmptyDomSnapshot_ReturnsNull()
    {
        _mockBrowser.Setup(b => b.QuerySelectorAsync("#missing", default))
            .ReturnsAsync((string?)null);
        _mockBrowser.Setup(b => b.EvaluateJsAsync(It.IsAny<string>(), default))
            .ReturnsAsync("[]");

        var step = MakeStep(
            selector: "#missing",
            fingerprint: MakeFingerprint(tag: "input", labelText: "Email"));
        var locator = CreateLocator();

        var result = await locator.LocateAsync(step, "test-template");

        Assert.Null(result.NodeId);
        Assert.False(result.DidRelocate);
    }

    [Fact]
    public async Task LocateAsync_SelectorMiss_FingerprintMatchButSelectorMiss_ReturnsNull()
    {
        _mockBrowser.Setup(b => b.QuerySelectorAsync("#old-email", default))
            .ReturnsAsync((string?)null);
        _mockBrowser.Setup(b => b.EvaluateJsAsync(It.IsAny<string>(), default))
            .ReturnsAsync("[{\"tag\":\"input\",\"attributes\":{\"type\":\"email\",\"name\":\"email\"},\"textContent\":\"\",\"labelText\":\"Email Address\",\"ancestors\":[\"form#app\"],\"siblingsBefore\":[\"First Name\"],\"siblingsAfter\":[\"Phone\"],\"positionIndex\":2,\"cssSelector\":\"input#new-email\"}]");
        _mockBrowser.Setup(b => b.QuerySelectorAsync("input#new-email", default))
            .ReturnsAsync((string?)null);

        var step = MakeStep(
            selector: "#old-email",
            fingerprint: MakeFingerprint(
                tag: "input",
                attributes: new Dictionary<string, string> { ["type"] = "email", ["name"] = "email" },
                labelText: "Email Address",
                ancestorChain: new List<string> { "form#app" }));

        var locator = CreateLocator();

        var result = await locator.LocateAsync(step, "test-template");

        Assert.Null(result.NodeId);
    }

    [Fact]
    public async Task LocateAsync_FallbackHit_NoFingerprintLookup()
    {
        _mockBrowser.Setup(b => b.QuerySelectorAsync("#email", default))
            .ReturnsAsync((string?)null);
        _mockBrowser.Setup(b => b.QuerySelectorAsync("input[type='email']", default))
            .ReturnsAsync("77");

        var step = MakeStep(
            selector: "#email",
            fallbackSelector: "input[type='email']",
            fingerprint: MakeFingerprint(tag: "input"));

        var locator = CreateLocator();

        var result = await locator.LocateAsync(step, "test-template");

        Assert.Equal("77", result.NodeId);
        Assert.Equal("input[type='email']", result.ResolvedSelector);
        Assert.False(result.DidRelocate);

        _mockBrowser.Verify(b => b.EvaluateJsAsync(It.IsAny<string>(), default), Times.Never);
    }
}