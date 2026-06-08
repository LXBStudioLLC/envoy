using Envoy.Core.Services;
using Xunit;

namespace Envoy.Core.Tests;

public class DomScorerTests
{
    private static Fingerprint MakeFingerprint(
        string? tag = null,
        Dictionary<string, string>? attributes = null,
        string? labelText = null,
        string? textContent = null,
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
            TextContent = textContent,
            AncestorChain = ancestorChain,
            SiblingsBefore = siblingsBefore,
            SiblingsAfter = siblingsAfter,
            PositionIndex = positionIndex
        };
    }

    private static DomCandidate MakeCandidate(
        string tag = "input",
        Dictionary<string, string>? attributes = null,
        string? labelText = null,
        string? textContent = null,
        List<string>? ancestorChain = null,
        List<string>? siblingsBefore = null,
        List<string>? siblingsAfter = null,
        int positionIndex = 0,
        string cssSelector = "input#test")
    {
        return new DomCandidate
        {
            Tag = tag,
            Attributes = attributes ?? new Dictionary<string, string>(),
            LabelText = labelText ?? "",
            TextContent = textContent,
            AncestorChain = ancestorChain ?? new List<string>(),
            SiblingsBefore = siblingsBefore ?? new List<string>(),
            SiblingsAfter = siblingsAfter ?? new List<string>(),
            PositionIndex = positionIndex,
            CssSelector = cssSelector
        };
    }

    [Fact]
    public void ScoreAttributes_ExactMatch_Returns1()
    {
        var fp = MakeFingerprint(attributes: new Dictionary<string, string>
        {
            ["type"] = "email",
            ["name"] = "email"
        });
        var candidate = MakeCandidate(attributes: new Dictionary<string, string>
        {
            ["type"] = "email",
            ["name"] = "email",
            ["id"] = "email-input"
        });

        var score = DomScorer.ScoreAttributes(fp, candidate);
        Assert.Equal(1.0, score, 2);
    }

    [Fact]
    public void ScoreAttributes_PartialMatch_ReturnsProportional()
    {
        var fp = MakeFingerprint(attributes: new Dictionary<string, string>
        {
            ["type"] = "email",
            ["name"] = "different"
        });
        var candidate = MakeCandidate(attributes: new Dictionary<string, string>
        {
            ["type"] = "email",
            ["name"] = "email"
        });

        var score = DomScorer.ScoreAttributes(fp, candidate);
        Assert.True(score > 0);
        Assert.True(score < 1.0);
    }

    [Fact]
    public void ScoreAttributes_NoFingerprintAttributes_Returns0()
    {
        var fp = MakeFingerprint();
        var candidate = MakeCandidate(attributes: new Dictionary<string, string> { ["type"] = "email" });

        var score = DomScorer.ScoreAttributes(fp, candidate);
        Assert.Equal(0, score);
    }

    [Fact]
    public void ScoreAttributes_ImportantAttributesWeightedHigher()
    {
        var fpName = MakeFingerprint(
            attributes: new Dictionary<string, string> { ["name"] = "email", ["class"] = "form-input" });
        var fpClass = MakeFingerprint(
            attributes: new Dictionary<string, string> { ["class"] = "form-input", ["name"] = "different" });

        var candidate = MakeCandidate(attributes: new Dictionary<string, string>
        {
            ["name"] = "email",
            ["class"] = "form-input"
        });

        var scoreName = DomScorer.ScoreAttributes(fpName, candidate);
        var scoreClass = DomScorer.ScoreAttributes(fpClass, candidate);

        Assert.True(scoreName > scoreClass, $"name match should score higher than class-only match: {scoreName} vs {scoreClass}");
    }

    [Fact]
    public void ScoreLabel_ExactMatch_Returns1()
    {
        var fp = MakeFingerprint(labelText: "Email Address");
        var candidate = MakeCandidate(labelText: "Email Address");

        var score = DomScorer.ScoreLabel(fp, candidate);
        Assert.Equal(1.0, score, 2);
    }

    [Fact]
    public void ScoreLabel_SimilarMatch_ReturnsHighScore()
    {
        var fp = MakeFingerprint(labelText: "Email Address");
        var candidate = MakeCandidate(labelText: "Email address");

        var score = DomScorer.ScoreLabel(fp, candidate);
        Assert.True(score > 0.8);
    }

    [Fact]
    public void ScoreLabel_DifferentText_ReturnsLowScore()
    {
        var fp = MakeFingerprint(labelText: "Email Address");
        var candidate = MakeCandidate(labelText: "First Name");

        var score = DomScorer.ScoreLabel(fp, candidate);
        Assert.True(score < 0.5);
    }

    [Fact]
    public void ScoreLabel_EmptyFingerprintLabel_Returns0()
    {
        var fp = MakeFingerprint(labelText: null);
        var candidate = MakeCandidate(labelText: "Email Address");

        var score = DomScorer.ScoreLabel(fp, candidate);
        Assert.Equal(0, score);
    }

    [Fact]
    public void ScoreAncestors_ExactSuffixMatch_Returns1()
    {
        var fp = MakeFingerprint(ancestorChain: new List<string> { "form#app", "div.field" });
        var candidate = MakeCandidate(ancestorChain: new List<string> { "form#app", "div.field" });

        var score = DomScorer.ScoreAncestors(fp, candidate);
        Assert.Equal(1.0, score, 2);
    }

    [Fact]
    public void ScoreAncestors_PartialSuffixMatch_ReturnsProportional()
    {
        var fp = MakeFingerprint(ancestorChain: new List<string> { "div#app", "form.application" });
        var candidate = MakeCandidate(ancestorChain: new List<string> { "main.content", "div#app", "form.application" });

        var score = DomScorer.ScoreAncestors(fp, candidate);
        Assert.Equal(1.0, score, 2);
    }

    [Fact]
    public void ScoreAncestors_PartialSuffixMatch_WhenNotExact_ReturnsProportional()
    {
        var fp = MakeFingerprint(ancestorChain: new List<string> { "main.content", "div#app", "form.application" });
        var candidate = MakeCandidate(ancestorChain: new List<string> { "nav.sidebar", "div#app", "form.application" });

        var score = DomScorer.ScoreAncestors(fp, candidate);
        Assert.True(score >= 0.5, $"Expected >= 0.5, got {score}");
        Assert.True(score < 1.0, $"Expected < 1.0, got {score}");
    }

    [Fact]
    public void ScoreAncestors_NoMatch_Returns0()
    {
        var fp = MakeFingerprint(ancestorChain: new List<string> { "nav.sidebar", "ul.menu" });
        var candidate = MakeCandidate(ancestorChain: new List<string> { "section.content", "div.field" });

        var score = DomScorer.ScoreAncestors(fp, candidate);
        Assert.Equal(0, score, 2);
    }

    [Fact]
    public void ScoreSiblings_ExactMatch_Returns1()
    {
        var fp = MakeFingerprint(
            siblingsBefore: new List<string> { "First Name", "Last Name" },
            siblingsAfter: new List<string> { "Phone" });
        var candidate = MakeCandidate(
            siblingsBefore: new List<string> { "First Name", "Last Name" },
            siblingsAfter: new List<string> { "Phone" });

        var score = DomScorer.ScoreSiblings(fp, candidate);
        Assert.Equal(1.0, score, 2);
    }

    [Fact]
    public void ScoreSiblings_PartialMatch_ReturnsProportional()
    {
        var fp = MakeFingerprint(
            siblingsBefore: new List<string> { "First Name", "Last Name" },
            siblingsAfter: new List<string> { "Phone" });
        var candidate = MakeCandidate(
            siblingsBefore: new List<string> { "First Name", "Company" },
            siblingsAfter: new List<string> { "Address" });

        var score = DomScorer.ScoreSiblings(fp, candidate);
        Assert.True(score > 0);
        Assert.True(score < 1.0);
    }

    [Fact]
    public void ScorePosition_ExactMatch_Returns1()
    {
        var fp = MakeFingerprint(positionIndex: 2);
        var candidate = MakeCandidate(positionIndex: 2);

        var score = DomScorer.ScorePosition(fp, candidate);
        Assert.Equal(1.0, score, 2);
    }

    [Fact]
    public void ScorePosition_OffByOne_Returns9()
    {
        var fp = MakeFingerprint(positionIndex: 2);
        var candidate = MakeCandidate(positionIndex: 1);

        var score = DomScorer.ScorePosition(fp, candidate);
        Assert.True(score >= 0.9);
    }

    [Fact]
    public void ScorePosition_LargeGap_ReturnsLowScore()
    {
        var fp = MakeFingerprint(positionIndex: 0);
        var candidate = MakeCandidate(positionIndex: 10);

        var score = DomScorer.ScorePosition(fp, candidate);
        Assert.True(score <= 0.1);
    }

    [Fact]
    public void ScorePosition_NullFingerprintPosition_Returns0()
    {
        var fp = MakeFingerprint(positionIndex: null);
        var candidate = MakeCandidate(positionIndex: 5);

        var score = DomScorer.ScorePosition(fp, candidate);
        Assert.Equal(0, score);
    }

    [Fact]
    public void ScoreCandidate_WeightsCombinedCorrectly()
    {
        var fp = MakeFingerprint(
            tag: "input",
            attributes: new Dictionary<string, string> { ["type"] = "email", ["name"] = "email" },
            labelText: "Email Address",
            ancestorChain: new List<string> { "form#app", "div.field" },
            siblingsBefore: new List<string> { "First Name" },
            siblingsAfter: new List<string> { "Phone" },
            positionIndex: 2);

        var candidate = MakeCandidate(
            tag: "input",
            attributes: new Dictionary<string, string> { ["type"] = "email", ["name"] = "email" },
            labelText: "Email Address",
            ancestorChain: new List<string> { "form#app", "div.field" },
            siblingsBefore: new List<string> { "First Name" },
            siblingsAfter: new List<string> { "Phone" },
            positionIndex: 2,
            cssSelector: "input#email");

        var score = DomScorer.ScoreCandidate(fp, candidate);
        Assert.True(score >= 0.95, $"Expected score >= 0.95, got {score}");
    }

    [Fact]
    public void FindBestMatch_TagFilter_FiltersNonMatchingTags()
    {
        var fp = MakeFingerprint(
            tag: "input",
            attributes: new Dictionary<string, string> { ["type"] = "email" });
        var candidates = new List<DomCandidate>
        {
            MakeCandidate(tag: "input", attributes: new Dictionary<string, string> { ["type"] = "email" }, cssSelector: "input#email"),
            MakeCandidate(tag: "button", cssSelector: "button#submit"),
            MakeCandidate(tag: "select", cssSelector: "select#country")
        };

        var result = DomScorer.FindBestMatch(fp, candidates, 0.5);
        Assert.NotNull(result);
        Assert.Contains("input", result.CssSelector);
        Assert.DoesNotContain("button", result.CssSelector);
        Assert.DoesNotContain("select", result.CssSelector);
    }

    [Fact]
    public void FindBestMatch_AboveThreshold_ReturnsMatch()
    {
        var fp = MakeFingerprint(
            tag: "input",
            attributes: new Dictionary<string, string> { ["type"] = "email", ["name"] = "email" },
            labelText: "Email Address");

        var candidates = new List<DomCandidate>
        {
            MakeCandidate(
                tag: "input",
                attributes: new Dictionary<string, string> { ["type"] = "email", ["name"] = "email" },
                labelText: "Email Address",
                cssSelector: "input#email")
        };

        var result = DomScorer.FindBestMatch(fp, candidates, 0.5);
        Assert.NotNull(result);
        Assert.True(result.AboveThreshold);
        Assert.Equal("input#email", result.CssSelector);
    }

    [Fact]
    public void FindBestMatch_BelowThreshold_ReturnsResultWithBelowFlag()
    {
        var fp = MakeFingerprint(
            tag: "input",
            attributes: new Dictionary<string, string> { ["type"] = "email" },
            labelText: "Email Address");

        var candidates = new List<DomCandidate>
        {
            MakeCandidate(
                tag: "input",
                attributes: new Dictionary<string, string> { ["type"] = "text" },
                labelText: "Company Name",
                cssSelector: "input#company")
        };

        var result = DomScorer.FindBestMatch(fp, candidates, 0.95);
        Assert.NotNull(result);
        Assert.False(result.AboveThreshold);
    }

    [Fact]
    public void FindBestMatch_NoCandidates_ReturnsNull()
    {
        var fp = MakeFingerprint(tag: "input");
        var candidates = new List<DomCandidate>();

        var result = DomScorer.FindBestMatch(fp, candidates, 0.5);
        Assert.Null(result);
    }

    [Fact]
    public void FindBestMatch_TagFilterEliminatesAll_ReturnsNull()
    {
        var fp = MakeFingerprint(tag: "textarea");
        var candidates = new List<DomCandidate>
        {
            MakeCandidate(tag: "input", cssSelector: "input#email"),
            MakeCandidate(tag: "button", cssSelector: "button#submit")
        };

        var result = DomScorer.FindBestMatch(fp, candidates, 0.5);
        Assert.Null(result);
    }
}