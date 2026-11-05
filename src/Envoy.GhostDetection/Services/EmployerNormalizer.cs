namespace Envoy.GhostDetection.Services;

/// <summary>
/// Shared employer-name normalization used by signals that fuzzy-match company
/// names against external datasets (WARN notices, PERM filings). Both
/// <see cref="Signals.HiringFreezeSignal"/> and
/// <see cref="Signals.PermCrossCheckSignal"/> compare normalized names with
/// <c>DomScorer.NormalizedSimilarity</c> at the same ≥ 0.85 bar.
/// </summary>
public static class EmployerNormalizer
{
    private static readonly string[] LegalSuffixes =
    {
        "incorporated", "inc", "llc", "llp", "lp", "plc", "ltd",
        "corp", "corporation", "company", "co", "limited", "holdings", "group", "dba"
    };

    /// <summary>
    /// Lowercases, strips punctuation, and drops a trailing legal entity suffix
    /// ("Acme Widgets Inc." → "acme widgets"; "Foo, Inc. dba Bar" → "foo").
    /// Returns the normalized name as space-joined lowercase tokens.
    /// </summary>
    public static string Normalize(string? employer)
    {
        if (string.IsNullOrWhiteSpace(employer))
            return string.Empty;

        var sb = new System.Text.StringBuilder(employer.Length);
        foreach (var ch in employer)
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');

        var tokens = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(tokens.Length);
        var dbaIndex = Array.IndexOf(tokens, "dba");
        var take = dbaIndex >= 0 ? dbaIndex : tokens.Length;

        for (var i = 0; i < take; i++)
        {
            // A trailing legal-suffix token is dropped ("...USA, LLC." -> "...usa").
            if (i == take - 1 && LegalSuffixes.Contains(tokens[i]))
                continue;
            kept.Add(tokens[i]);
        }

        return string.Join(' ', kept);
    }
}
