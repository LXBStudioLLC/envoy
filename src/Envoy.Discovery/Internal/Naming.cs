namespace Envoy.Discovery.Internal;

internal static class Naming
{
    /// <summary>Turns a board token like "match-group" into a display name "Match Group".</summary>
    public static string Prettify(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return token;
        var parts = token.Replace('-', ' ').Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Select(w =>
            w.Length <= 2 ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
