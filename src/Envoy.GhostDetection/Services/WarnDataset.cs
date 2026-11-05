using System.Text.Json;

namespace Envoy.GhostDetection.Services;

/// <summary>
/// One WARN Act notice from the bundled (or user-refreshed) dataset.
/// Employer-level data only: WARN notices do not carry role detail.
/// </summary>
public sealed record WarnNotice
{
    public string Employer { get; init; } = string.Empty;

    /// <summary>Two-letter U.S. state code, e.g. "WA".</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Free-text location as published by the state agency (optional).</summary>
    public string? Location { get; init; }

    /// <summary>Date the state agency posted/received the notice (ISO yyyy-MM-dd).</summary>
    public DateTime NoticeDate { get; init; }

    /// <summary>Workers affected, 0 when the agency did not publish a count.</summary>
    public int AffectedCount { get; init; }
}

/// <summary>
/// Local WARN Act notice dataset used by <c>HiringFreezeSignal</c>.
///
/// <para>Load order:
/// 1. the user-refreshed cache at <c>%LOCALAPPDATA%\Envoy\warn-notices.json</c>
///    (written by <see cref="WarnRefresher"/>),
/// 2. the bundled seed <c>warn-notices.json</c> next to the app,
/// 3. an empty list (the signal then simply never fires — graceful, never throws).</para>
///
/// <para>The loaded list is cached for the process lifetime; contents are real
/// public data but naturally age out of the signal's 90-day window, which is why
/// the bundled seed must eventually be refreshed by a refresher.</para>
/// </summary>
public static class WarnDataset
{
    private const string FileName = "warn-notices.json";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static volatile IReadOnlyList<WarnNotice>? _cached;

    /// <summary>The test hook: when set, <see cref="Load"/> returns this instead of touching disk.</summary>
    public static Func<IReadOnlyList<WarnNotice>?>? LoadOverride;

    public static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Envoy", FileName);

    public static string BundledPath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static IReadOnlyList<WarnNotice> Load()
    {
        // The test hook is deliberately re-evaluated per call and not cached,
        // so tests never leak their dataset into other tests.
        if (LoadOverride is not null)
            return LoadOverride() ?? Array.Empty<WarnNotice>();

        if (_cached is not null)
            return _cached;

        Gate.Wait();
        try
        {
            if (_cached is not null)
                return _cached;

            List<WarnNotice>? notices = null;

            try
            {
                if (File.Exists(CachePath))
                    notices = ReadFile(CachePath);
                else if (File.Exists(BundledPath))
                    notices = ReadFile(BundledPath);
            }
            catch
            {
                notices = null; // any load failure degrades to "no data", never throws
            }

            var result = (IReadOnlyList<WarnNotice>)(notices ?? new List<WarnNotice>());
            _cached = result;
            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Replaces the in-memory dataset after a <see cref="WarnRefresher"/> run
    /// (the refresher itself writes the cache file; this keeps the live
    /// process in step so the very next posting scores with fresh data).
    /// Passing <c>null</c> resets to an empty dataset.
    /// </summary>
    public static void Replace(IReadOnlyList<WarnNotice>? notices)
    {
        Gate.Wait();
        try
        {
            _cached = (IReadOnlyList<WarnNotice>)(notices ?? new List<WarnNotice>());
        }
        finally
        {
            Gate.Release();
        }
    }

    private static List<WarnNotice>? ReadFile(string path)
    {
        var json = File.ReadAllText(path);

        // Tolerate `//` comment lines the same way the fixture loader does.
        var stripped = string.Join('\n', json.Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
        if (string.IsNullOrWhiteSpace(stripped))
            return null;

        var notices = JsonSerializer.Deserialize<List<WarnNotice>>(stripped);
        return notices is { Count: > 0 } ? notices : null;
    }
}
