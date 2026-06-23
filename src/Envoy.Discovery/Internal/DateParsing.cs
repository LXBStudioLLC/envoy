using System.Globalization;

namespace Envoy.Discovery.Internal;

internal static class DateParsing
{
    /// <summary>Parses an ISO-8601 timestamp to UTC, or null.</summary>
    public static DateTime? Iso(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto)
            ? dto.UtcDateTime
            : null;
    }

    /// <summary>Parses a Unix epoch in MILLISECONDS (Lever's createdAt) to UTC, or null.</summary>
    public static DateTime? UnixMs(long ms) =>
        ms <= 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;

    private static readonly string[] RecruiteeFormats =
    {
        "yyyy-MM-dd HH:mm:ss 'UTC'",
        "yyyy-MM-dd HH:mm:ss"
    };

    /// <summary>Parses Recruitee's non-ISO "yyyy-MM-dd HH:mm:ss UTC" timestamp, or null.</summary>
    public static DateTime? Recruitee(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParseExact(value, RecruiteeFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? dt
            : Iso(value);
    }
}
