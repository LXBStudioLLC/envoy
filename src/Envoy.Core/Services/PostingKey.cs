namespace Envoy.Core.Services;

/// <summary>
/// Builds the stable identity key that lets Envoy recognize the same posting
/// across sessions (the scoreboard ledger today, repost-frequency history
/// later). Prefers the URL with host, path, and query canonicalized and
/// tracking noise stripped; falls back to company + title when there is no
/// usable URL.
/// </summary>
public static class PostingKey
{
    // Query parameters that vary per click without changing which job the URL
    // points at. Meaningful ids (e.g. Greenhouse's gh_jid) are kept.
    private static readonly HashSet<string> TrackingParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "ref", "referrer", "source", "src", "gclid", "fbclid", "mc_cid", "mc_eid"
    };

    public static string For(string? jobUrl, string? company, string? jobTitle)
    {
        if (!string.IsNullOrWhiteSpace(jobUrl)
            && Uri.TryCreate(jobUrl.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var host = uri.Host.ToLowerInvariant();
            if (host.StartsWith("www.", StringComparison.Ordinal))
                host = host[4..];
            var path = uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            var query = CanonicalQuery(uri.Query.ToLowerInvariant());
            return host + path + query;
        }

        // No usable URL: normalized company|title pair. Distinct unknown
        // postings can collide here; acceptable for stats, and better than
        // dropping the event.
        return Normalize(company) + "|" + Normalize(jobTitle);
    }

    // Drops tracking parameters and sorts the survivors so the same posting
    // reached via reordered or campaign-decorated links produces one key.
    private static string CanonicalQuery(string query)
    {
        if (string.IsNullOrEmpty(query) || query == "?") return "";

        var kept = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair =>
            {
                var name = pair.Split('=', 2)[0];
                return name.Length > 0
                    && !TrackingParams.Contains(name)
                    && !name.StartsWith("utm_", StringComparison.Ordinal);
            })
            .OrderBy(pair => pair, StringComparer.Ordinal)
            .ToArray();

        return kept.Length == 0 ? "" : "?" + string.Join("&", kept);
    }

    private static string Normalize(string? value) =>
        string.Join(' ', (value ?? "").ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
