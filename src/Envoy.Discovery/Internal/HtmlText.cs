using System.Net;
using System.Text.RegularExpressions;

namespace Envoy.Discovery.Internal;

internal static class HtmlText
{
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Converts an HTML — or HTML-entity-escaped HTML, as Greenhouse returns — job
    /// description into clean plain text: decode entities, drop tags, collapse whitespace.
    /// </summary>
    public static string Strip(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var decoded = WebUtility.HtmlDecode(html);   // &lt;p&gt; -> <p>, &amp; -> &
        var noTags = Tags.Replace(decoded, " ");
        var text = WebUtility.HtmlDecode(noTags);     // resolve any entities left in text
        return Whitespace.Replace(text, " ").Trim();
    }
}
