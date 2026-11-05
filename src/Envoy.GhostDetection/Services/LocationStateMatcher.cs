namespace Envoy.GhostDetection.Services;

/// <summary>
/// Shared, conservative posting-location vs. U.S.-state matching used by
/// signals that compare a posting's Location against a dataset's state
/// (WARN notices, PERM filings). Same behavior for every caller:
/// <see cref="SignalStateMatch.Confirmed"/> only on clear evidence,
/// <see cref="SignalStateMatch.Mismatch"/> only on clear contradiction, and
/// <see cref="SignalStateMatch.Unknown"/> when the location carries no usable
/// state signal ("Remote", city-only, street address) — callers then stay at
/// employer-level matching and note the caveat in their evidence.
/// </summary>
public enum SignalStateMatch
{
    Confirmed,
    Mismatch,
    Unknown
}

public static class LocationStateMatcher
{
    private static readonly Dictionary<string, string> StateNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = "alabama", ["AK"] = "alaska", ["AZ"] = "arizona", ["AR"] = "arkansas",
        ["CA"] = "california", ["CO"] = "colorado", ["CT"] = "connecticut", ["DE"] = "delaware",
        ["FL"] = "florida", ["GA"] = "georgia", ["HI"] = "hawaii", ["ID"] = "idaho",
        ["IL"] = "illinois", ["IN"] = "indiana", ["IA"] = "iowa", ["KS"] = "kansas",
        ["KY"] = "kentucky", ["LA"] = "louisiana", ["ME"] = "maine", ["MD"] = "maryland",
        ["MA"] = "massachusetts", ["MI"] = "michigan", ["MN"] = "minnesota", ["MS"] = "mississippi",
        ["MO"] = "missouri", ["MT"] = "montana", ["NE"] = "nebraska", ["NV"] = "nevada",
        ["NH"] = "new hampshire", ["NJ"] = "new jersey", ["NM"] = "new mexico", ["NY"] = "new york",
        ["NC"] = "north carolina", ["ND"] = "north dakota", ["OH"] = "ohio", ["OK"] = "oklahoma",
        ["OR"] = "oregon", ["PA"] = "pennsylvania", ["RI"] = "rhode island", ["SC"] = "south carolina",
        ["SD"] = "south dakota", ["TN"] = "tennessee", ["TX"] = "texas", ["UT"] = "utah",
        ["VT"] = "vermont", ["VA"] = "virginia", ["WA"] = "washington", ["WV"] = "west virginia",
        ["WI"] = "wisconsin", ["WY"] = "wyoming", ["DC"] = "district of columbia"
    };

    /// <summary>
    /// Compare <paramref name="postingLocation"/> against <paramref name="state"/>
    /// (two-letter code). <paramref name="referenceCity"/> is an optional city /
    /// town or comma-separated list as published by the source dataset; when its
    /// first token shows up in the location the match is confirmed.
    /// </summary>
    public static SignalStateMatch Match(string? postingLocation, string? state, string? referenceCity = null)
    {
        var stateCode = state?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(stateCode) || !StateNames.ContainsKey(stateCode))
            return SignalStateMatch.Unknown;
        if (string.IsNullOrWhiteSpace(postingLocation))
            return SignalStateMatch.Unknown;

        var loc = postingLocation.ToLowerInvariant();

        var segments = postingLocation.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var trailingCode = segments.Length > 0 ? segments[^1].Trim().ToUpperInvariant() : string.Empty;

        // "Redmond, WA" vs a WA filing
        if (trailingCode.Length == 2 && trailingCode == stateCode)
            return SignalStateMatch.Confirmed;

        // Full state name in the location, e.g. "1 Hacker Way, California"
        if (StateNames.TryGetValue(stateCode, out var stateName) &&
            loc.Contains(stateName, StringComparison.OrdinalIgnoreCase))
            return SignalStateMatch.Confirmed;

        // The dataset's city/town appearing in the location, e.g. notice "Redmond"
        if (!string.IsNullOrWhiteSpace(referenceCity))
        {
            var city = referenceCity.Split(',')[0].Trim();
            if (city.Length >= 4 && loc.Contains(city, StringComparison.OrdinalIgnoreCase))
                return SignalStateMatch.Confirmed;
        }

        // Any other full state name in the location, e.g. "Miami, Florida" vs CA
        foreach (var (code, name) in StateNames)
        {
            if (code != stateCode && loc.Contains(name, StringComparison.OrdinalIgnoreCase))
                return SignalStateMatch.Mismatch;
        }

        // "Redmond, OR" vs a WA filing
        if (trailingCode.Length == 2 && StateNames.ContainsKey(trailingCode) && trailingCode != stateCode)
            return SignalStateMatch.Mismatch;

        // No usable state signal → employer-level match with a caveat.
        return SignalStateMatch.Unknown;
    }

    /// <summary>Two-letter code → full state name, or null when unknown.</summary>
    public static string? StateName(string? code)
    {
        var c = code?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(c) || !StateNames.TryGetValue(c, out var name))
            return null;
        return name;
    }
}
