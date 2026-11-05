using System.Text.Json;

namespace Envoy.GhostDetection.Services;

/// <summary>
/// One PERM labor-certification filing from the bundled (or user-refreshed)
/// pre-shrunk DOL OFLC disclosure index.
/// </summary>
public sealed record PermFiling
{
    public string Employer { get; init; } = string.Empty;
    public string JobTitle { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string SocCode { get; init; } = string.Empty;
    public DateTime ReceivedDate { get; init; }
}

/// <summary>
/// The local, pre-shrunk PERM index used by <c>PermCrossCheckSignal</c>.
///
/// <para>The bundled seed (<c>perm-index.json</c>, ~1,600 filings) is generated
/// OUT-OF-BAND from the public DOL OFLC PERM disclosure data (the raw quarterly
/// file is hundreds of MB and must never be shipped or parsed per-posting).
/// The loader reads it into memory once: cache at
/// <c>%LOCALAPPDATA%\Envoy\perm-index.json</c> (a future
/// <c>PermIndexBuilder</c> refresher writes there), then the bundled seed, then
/// an empty list (the signal then never fires — graceful, never throws).

/// <para>This loader performs NO network access. Evaluation never fetches.</para>
/// </summary>
public static class PermIndex
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static volatile IReadOnlyList<PermFiling>? _cached;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private sealed record IndexEnvelope
    {
        public string? GeneratedAt { get; set; }
        public string? Source { get; set; }
        public string? ReceivedFrom { get; set; }
        // Rows are [Employer, JobTitle, State, SocCode, ReceivedDate] per ColumnOrder.
        public List<JsonElement> Rows { get; set; } = new();
    }

    /// <summary>The test hook: when set, <see cref="Load"/> returns this instead of touching disk.</summary>
    public static Func<IReadOnlyList<PermFiling>?>? LoadOverride;

    public static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Envoy", "perm-index.json");

    public static string BundledPath => Path.Combine(AppContext.BaseDirectory, "perm-index.json");

    public static IReadOnlyList<PermFiling> Load()
    {
        // The test hook is deliberately re-evaluated per call and not cached.
        if (LoadOverride is not null)
            return LoadOverride() ?? Array.Empty<PermFiling>();

        if (_cached is not null)
            return _cached;

        Gate.Wait();
        try
        {
            _cached ??= LoadFromDisk();
            return _cached;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Replaces the in-memory index after a <see cref="PermIndexBuilder"/> run
    /// (the builder writes the cache file; this keeps the live process in step
    /// so the next posting scores with fresh data). Passing <c>null</c> is
    /// allowed and resets to an empty index. Mirrors <c>WarnDataset.Replace</c>.
    /// </summary>
    public static void Replace(IReadOnlyList<PermFiling>? filings)
    {
        Gate.Wait();
        try
        {
            _cached = (IReadOnlyList<PermFiling>)(filings ?? new List<PermFiling>());
        }
        finally
        {
            Gate.Release();
        }
    }

    private static IReadOnlyList<PermFiling> LoadFromDisk()
    {
        try
        {
            var path = File.Exists(CachePath)
                ? CachePath
                : File.Exists(BundledPath) ? BundledPath : null;
            if (path == null)
                return Array.Empty<PermFiling>();

            var envelope = JsonSerializer.Deserialize<IndexEnvelope>(File.ReadAllText(path), JsonOpts);
            if (envelope?.Rows is not { Count: > 0 })
                return Array.Empty<PermFiling>();

            var filings = new List<PermFiling>(envelope.Rows.Count);
            foreach (var row in envelope.Rows)
            {
                var f = ParseRow(row);
                if (f is not null)
                    filings.Add(f);
            }
            return filings;
        }
        catch
        {
            return Array.Empty<PermFiling>(); // any load failure degrades to "no data"
        }
    }

    private static PermFiling? ParseRow(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 5)
            return null;

        var employer = row[0].GetString() ?? string.Empty;
        var title = row[1].GetString() ?? string.Empty;
        var state = row[2].GetString() ?? string.Empty;
        var soc = row[3].GetString() ?? string.Empty;
        if (!DateTime.TryParse(row[4].GetString(), out var received))
            return null;
        if (string.IsNullOrWhiteSpace(employer) || string.IsNullOrWhiteSpace(title))
            return null;

        return new PermFiling { Employer = employer, JobTitle = title, State = state, SocCode = soc, ReceivedDate = received };
    }
}
