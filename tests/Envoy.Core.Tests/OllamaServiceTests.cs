using Envoy.Core.Services;
using Xunit;

namespace Envoy.Core.Tests;

public class OllamaServiceTests
{
    [Fact]
    public void ExtractJson_SimpleObject_ReturnsObject()
    {
        var input = "Here is the result: {\"Name\": \"John\", \"Age\": 30} and that's it.";
        var result = OllamaService.ExtractJson(input);

        Assert.Equal("{\"Name\": \"John\", \"Age\": 30}", result);
    }

    [Fact]
    public void ExtractJson_NestedObject_ReturnsFullObject()
    {
        var input = "{\"Outer\": {\"Inner\": \"Value\"}, \"Count\": 5}";
        var result = OllamaService.ExtractJson(input);

        Assert.Equal("{\"Outer\": {\"Inner\": \"Value\"}, \"Count\": 5}", result);
    }

    [Fact]
    public void ExtractJson_BracesInString_DoesNotBreak()
    {
        var input = "{\"Summary\": \"Used \\frac{x}{y} in calculations\", \"Name\": \"Test\"}";
        var result = OllamaService.ExtractJson(input);

        Assert.Contains("Summary", result);
        Assert.Contains("Name", result);
    }

    [Fact]
    public void ExtractJson_EscapedQuotes_DoesNotBreak()
    {
        var input = "{\"Text\": \"He said \\\"hello\\\" to her\", \"Count\": 1}";
        var result = OllamaService.ExtractJson(input);

        Assert.Contains("Text", result);
        Assert.Contains("Count", result);
    }

    [Fact]
    public void ExtractJson_MultipleObjects_ReturnsFirstComplete()
    {
        var input = "```json\n{\"First\": 1}\n```\nSome other text\n{\"Second\": 2}";
        var result = OllamaService.ExtractJson(input);

        Assert.Equal("{\"First\": 1}", result);
    }

    [Fact]
    public void ExtractJson_NoObject_ReturnsOriginal()
    {
        var input = "No JSON here, just plain text.";
        var result = OllamaService.ExtractJson(input);

        Assert.Equal("No JSON here, just plain text.", result);
    }

    [Fact]
    public void ExtractJson_ArrayInObject_HandlesCorrectly()
    {
        var input = "{\"Skills\": [\"C#\", \"Python\"], \"Name\": \"Test\"}";
        var result = OllamaService.ExtractJson(input);

        Assert.Equal("{\"Skills\": [\"C#\", \"Python\"], \"Name\": \"Test\"}", result);
    }

    [Fact]
    public void ExtractJson_BraceInsideQuotedString_DoesNotCount()
    {
        var input = "{\"Message\": \"{hello}\", \"Status\": \"ok\"}";
        var result = OllamaService.ExtractJson(input);

        Assert.Equal("{\"Message\": \"{hello}\", \"Status\": \"ok\"}", result);
    }

    [Fact]
    public void ExtractJson_TrailingBraceInsideString_DoesNotCloseEarly()
    {
        var input = "{\"Text\": \"a}b\", \"Val\": 1}";
        var result = OllamaService.ExtractJson(input);

        Assert.Equal("{\"Text\": \"a}b\", \"Val\": 1}", result);
    }

    [Fact]
    public void ExtractJson_EmbeddedJsonInString_DoesNotNest()
    {
        var input = "{\"Outer\": \"{\\\"Inner\\\": 1}\", \"Count\": 2}";
        var result = OllamaService.ExtractJson(input);

        Assert.Contains("Outer", result);
        Assert.Contains("Count", result);
    }
}