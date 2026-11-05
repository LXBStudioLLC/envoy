using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace Envoy.GhostDetection.Services;

/// <summary>Outcome of a PERM index build attempt (never throws).</summary>
public sealed class PermIndexBuildResult
{
    public bool Success { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string? Error { get; init; }
    public int Total { get; init; }
    public string? SourceUrl { get; init; }
}

/// <summary>
/// Regenerates the local, pre-shrunk PERM filing index that
/// <c>PermCrossCheckSignal</c> reads (<c>%LOCALAPPDATA%\Envoy\perm-index.json</c>,
/// first in <see cref="PermIndex"/>'s load order — the bundled seed stays the
/// fallback and is never overwritten).
///
/// <para>Flow: download the newest applicable **public** DOL OFLC PERM quarterly
/// disclosure XLSX (dol.gov 403s generic crawler user-agents; the request sets a
/// plain "curl/8.0.1" UA, mirroring how the bundled seed was originally
/// fetched), then stream-parse the workbook with <see cref="ZipArchive"/> +
/// <see cref="XmlReader"/> (no Excel package dependency, flat memory even for
/// the ~156 MB file), filter to filings received in the last 12 months, dedupe
/// on (normalized employer, lowercase title, state) keeping the most recent
/// filing date, sanity-gate the result, and write the same envelope the
/// loader knows.</para>
///
/// <para>This is a background maintenance step, deliberately NOT part of any
/// signal's <c>EvaluateAsync</c>: per-posting evaluation stays local and
/// network-free. The source is public government data; nothing here logs in or
/// bypasses a bot-check — a hard block is surfaced and the build stops.</para>
/// </summary>
public sealed class PermIndexBuilder
{
    /// <summary>
    /// Candidate disclosure files, newest first. OFLC's fiscal year starts
    /// Oct 1, and the quarterly files are cumulative over received cases, so
    /// the newest quarter of a fiscal year covers every row a 12-month window
    /// needs. Proven URL: FY2026 Q3 (the file the bundled seed was cut from).
    /// </summary>
    private static string BuildCandidateUrl(int fiscalYear, int quarter) =>
        $"https://www.dol.gov/media/PERM_Disclosure_Data_FY{fiscalYear}_Q{quarter}.xlsx";

    /// <summary>Newest-first candidate URLs for a given date; frozen for tests.</summary>
    public static IReadOnlyList<string> CandidateSourceUrls(DateTime now)
    {
        var fiscalYear = now.Month >= 10 ? now.Year + 1 : now.Year;
        var currentQuarter = now.Month switch
        {
            10 or 11 or 12 => 1,
            1 or 2 or 3 => 2,
            4 or 5 or 6 => 3,
            _ => 4
        };

        var urls = new List<string>(7);
        for (var q = currentQuarter; q >= 1; q--)
            urls.Add(BuildCandidateUrl(fiscalYear, q));

        // Previous fiscal year as backup (covers slack in the publishing cadence).
        var previous = fiscalYear - 1;
        for (var q = 4; q >= 1; q--)
            urls.Add(BuildCandidateUrl(previous, q));

        return urls.Distinct().ToList();
    }

    /// <summary>Minimum rows the shrink must keep before the index is accepted.</summary>
    public const int MinRows = 500;

    private readonly HttpClient _http;
    private readonly ILogger<PermIndexBuilder>? _logger;

    /// <summary>
    /// Where the refreshed index is written. Defaults to the user cache that
    /// <see cref="PermIndex"/> reads first; tests redirect it.
    /// </summary>
    public string CachePath { get; init; } = PermIndex.CachePath;

    public PermIndexBuilder(HttpClient http, ILogger<PermIndexBuilder>? logger = null)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<PermIndexBuildResult> BuildAsync(
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var candidates = CandidateSourceUrls(DateTime.UtcNow);
        string? lastError = null;
        var cutoff = DateTime.UtcNow.AddMonths(-12);

        try
        {
            foreach (var url in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var download = await DownloadWorkbookAsync(url, progress, cancellationToken);
                if (download.Error is not null || download.TempPath is null)
                {
                    lastError = $"{url}: {download.Error ?? "empty download"}";
                    continue;
                }

                await using var workbook = File.OpenRead(download.TempPath);
                var (shrink, shrinkError) = Shrink(workbook, cutoff);
                TryDelete(download.TempPath); // parse is done; free the space either way
                if (shrinkError is not null)
                {
                    lastError = $"{url}: {shrinkError}";
                    continue;
                }

                WriteCache(url, cutoff, shrink.Filings);
                PermIndex.Replace(shrink.Filings);

                var stateCount = shrink.Filings.Select(f => f.State).Where(s => s.Length > 0).Distinct().Count();
                _logger?.LogInformation(
                    "PERM index built from {Url}: {Count} filings (received {From} → {To})",
                    url, shrink.Filings.Count, shrink.ReceivedFrom.ToString("yyyy-MM-dd"), shrink.ReceivedTo.ToString("yyyy-MM-dd"));

                return new PermIndexBuildResult
                {
                    Success = true,
                    SourceUrl = url,
                    Total = shrink.Filings.Count,
                    Summary = $"PERM index refreshed from DOL OFLC ({url}): {shrink.Filings.Count} filings received " +
                              $"{shrink.ReceivedFrom:yyyy-MM-dd} → {shrink.ReceivedTo:yyyy-MM-dd} across {stateCount} states."
                };
            }

            return new PermIndexBuildResult
            {
                Success = false,
                Error = $"No candidate disclosure file could be used. Last error: {lastError}"
            };
        }
        catch (OperationCanceledException)
        {
            return new PermIndexBuildResult { Success = false, Error = "PERM index refresh cancelled." };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "PERM index build failed");
            return new PermIndexBuildResult { Success = false, Error = ex.Message };
        }
    }

    // ── Download ─────────────────────────────────────────────────────────

    /// <summary>dol.gov 403s generic crawler user-agents; the seed was fetched with a curl UA.</summary>
    private static readonly string DownloadUserAgent = "curl/8.0.1";

    private async Task<(string? TempPath, string? Error)> DownloadWorkbookAsync(
        string url, IProgress<string>? progress, CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"envoy-perm-{Guid.NewGuid():N}.xlsx");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(DownloadUserAgent);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if ((int)response.StatusCode is 403 or 503)
                return (null, $"the server refused the request (HTTP {(int)response.StatusCode}); not retrying aggressively");

            if (!response.IsSuccessStatusCode)
                return (null, $"HTTP {(int)response.StatusCode}");

            var total = response.Content.Headers.ContentLength ?? -1;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

            byte[] chunk = new byte[1 << 18];
            var nextMilestone = 16L << 20;
            int read;
            while ((read = await source.ReadAsync(chunk, ct)) > 0)
            {
                await target.WriteAsync(chunk.AsMemory(0, read), ct);
                if (total > 0 && target.Length >= nextMilestone)
                {
                    progress?.Report($"Downloading {url.Split('/').Last()}: {target.Length / (1024 * 1024)} MB of {total / (1024 * 1024):0} MB");
                    nextMilestone += 16L << 20;
                }
            }

            return (tempPath, null);
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            if (ex is OperationCanceledException) throw;
            return (null, ex.Message);
        }
    }

    private static void TryDelete(string? path)
    {
        try { if (path is not null && File.Exists(path)) File.Delete(path); }
        catch { /* never fail the flow on a temp-file cleanup miss */ }
    }

    // ── Shrink (streaming XLSX → rows) ───────────────────────────────────

    /// <summary>Result of <see cref="Shrink"/>: rows in the exact envelope shape plus sanity data.</summary>
    public readonly record struct ShrinkSuccess(
        IReadOnlyList<PermFiling> Filings, DateTime ReceivedFrom, DateTime ReceivedTo);

    /// <summary>Shrink a disclosure workbook's bytes into the local row shape.</summary>
    public static (ShrinkSuccess Success, string? Error) Shrink(byte[] xlsxBytes, DateTime cutoff)
    {
        using var stream = new MemoryStream(xlsxBytes);
        return Shrink(stream, cutoff);
    }

    /// <summary>
    /// Shrink a disclosure workbook into the local row shape. Two streaming
    /// passes over the same stream (pass 1: header row + shared strings, pass
    /// 2: data rows), so memory stays flat for a multi-hundred-MB file.
    /// </summary>
    public static (ShrinkSuccess Success, string? Error) Shrink(Stream xlsx, DateTime cutoff)
    {
        try
        {
            var columns = ReadWorkbookIndex(xlsx, out var sharedStrings);

            if (!columns.TryGetValue("received_date", out var receivedCol) ||
                (!columns.TryGetValue("emp_business_name", out var employerCol) &&
                 !columns.TryGetValue("emp_trade_name", out employerCol)) ||
                (!columns.TryGetValue("job_title", out var titleCol) &&
                 !columns.TryGetValue("pwd_soc_title", out titleCol)))
            {
                return (default, "required columns missing from the disclosure file (RECEIVED_DATE / employer name / job title)");
            }

            columns.TryGetValue("primary_worksite_state", out var stateCol);
            columns.TryGetValue("pwd_soc_code", out var socCol);

            // Second pass over the worksheet only (streaming, flat memory).
            xlsx.Position = 0;
            var rows = StreamRows(xlsx, sharedStrings);

            var mapping = new Dictionary<string, PermFiling>(StringComparer.OrdinalIgnoreCase);
            var receivedFrom = DateTime.MaxValue;
            var receivedTo = DateTime.MinValue;

            foreach (var row in rows)
            {
                var received = ParseCellDate(ColumnText(row, receivedCol));
                if (received is null || received < cutoff)
                    continue;

                var employer = ColumnText(row, employerCol);
                var title = ColumnText(row, titleCol);
                if (employer.Length == 0 || title.Length == 0)
                    continue;

                var state = ColumnText(row, stateCol).ToUpperInvariant();
                var soc = ColumnText(row, socCol);

                var key = $"{EmployerNormalizer.Normalize(employer)}|{title.ToLowerInvariant()}|{state}";
                if (mapping.TryGetValue(key, out var existing) && received <= existing.ReceivedDate)
                    continue;

                mapping[key] = new PermFiling
                {
                    Employer = employer,
                    JobTitle = title,
                    State = state,
                    SocCode = soc,
                    ReceivedDate = received.Value
                };

                if (received < receivedFrom) receivedFrom = received.Value;
                if (received > receivedTo) receivedTo = received.Value;
            }

            if (receivedFrom == DateTime.MaxValue) receivedFrom = default;
            if (receivedTo == DateTime.MinValue) receivedTo = default;

            if (mapping.Count < MinRows)
                return (default, $"sanity gate failed: only {mapping.Count} rows kept (minimum {MinRows}); refusing to write a bad index");

            var filings = mapping.Values
                .OrderByDescending(f => f.ReceivedDate)
                .ThenBy(f => f.Employer, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return (new ShrinkSuccess(filings, receivedFrom, receivedTo), null);
        }
        catch (Exception ex)
        {
            return (default, ex.Message);
        }
    }

    private static string ColumnText(string?[] row, int index) =>
        index >= 0 && index < row.Length ? row[index]?.Trim() ?? string.Empty : string.Empty;

    // ── Minimal streaming XLSX reader ────────────────────────────────────

    private static readonly XmlReaderSettings SheetSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null
    };

    private static Dictionary<string, int> ReadWorkbookIndex(Stream xlsx, out string?[]? sharedStrings)
    {
        sharedStrings = null;
        // leaveOpen: the caller re-positions the same stream for the row pass.
        using var zip = new ZipArchive(xlsx, ZipArchiveMode.Read, leaveOpen: true);

        var sharedEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (sharedEntry is not null)
        {
            using var shared = sharedEntry.Open();
            sharedStrings = ReadSharedStrings(shared);
        }

        var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml");
        if (sheetEntry is null)
            throw new InvalidDataException("no worksheet entry in the disclosure workbook");

        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        using (var reader = XmlReader.Create(sheetEntry.Open(), SheetSettings))
        {
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "row")
                {
                    // The first row is the header row; keys are lowercase.
                    var headerRow = ReadStringRow(reader, sharedStrings);
                    for (var i = 0; i < headerRow.Length; i++)
                    {
                        var name = headerRow[i]?.Trim().ToLowerInvariant();
                        if (string.IsNullOrEmpty(name))
                            continue;
                        if (!columns.ContainsKey(name))
                            columns[name] = i;
                    }
                    break;
                }
            }
        }

        return columns;
    }

    private static string?[] ReadSharedStrings(Stream stream)
    {
        var strings = new List<string?>(1 << 15);
        using var reader = XmlReader.Create(stream, SheetSettings);

        bool inSi = false, inT = false;
        var buffer = new StringBuilder();

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "si")
            {
                inSi = true;
                buffer.Clear();
                inT = false;
                continue;
            }
            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "si")
            {
                strings.Add(buffer.ToString());
                inSi = false;
                continue;
            }
            if (!inSi)
                continue;

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
                inT = true;
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "t")
                inT = false;
            else if (inT && reader.NodeType == XmlNodeType.Text)
                buffer.Append(reader.Value);
        }

        return strings.ToArray();
    }

    /// <summary>Streams data rows (resolved string arrays) from sheet1.xml.</summary>
    private static IEnumerable<string?[]> StreamRows(Stream xlsx, string?[]? sharedStrings)
    {
        using var zip = new ZipArchive(xlsx, ZipArchiveMode.Read, leaveOpen: true);
        var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml");
        if (sheetEntry is null)
            yield break;

        using var reader = XmlReader.Create(sheetEntry.Open(), SheetSettings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "row")
                continue;

            yield return ReadStringRow(reader, sharedStrings);
        }
    }

    /// <summary>Reads one &lt;row&gt; element fully, returning a column-indexed array of resolved values.</summary>
    private static string?[] ReadStringRow(XmlReader reader, string?[]? sharedStrings)
    {
        var cells = new List<(int Column, string? Value)>(16);
        var ordinal = 0;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "row")
                break;

            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "c")
                continue;

            var (column, value) = ReadCell(reader, sharedStrings, ordinal);
            ordinal++;
            if (value is not null)
                cells.Add((column, value));
        }

        var width = cells.Count == 0 ? 1 : cells.Max(c => c.Column) + 1;
        var resolved = new string?[width];
        foreach (var (column, value) in cells)
            resolved[column] = value;

        return resolved;
    }

    /// <summary>Reads one &lt;c&gt; cell fully: shared-string, inline-string, or raw text value.</summary>
    private static (int Column, string? Value) ReadCell(XmlReader reader, string?[]? sharedStrings, int fallbackColumn)
    {
        var reference = reader.GetAttribute("r") ?? string.Empty;
        var column = reference.Length == 0
            ? fallbackColumn
            : ColumnNumberFromReference(reference);
        var type = reader.GetAttribute("t") ?? "n";

        if (reader.IsEmptyElement)
            return (column, null);

        string? raw = null;
        string? inlineValue = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "c")
                break;

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "is")
            {
                inlineValue = ReadInlineText(reader);
                continue;
            }

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "v")
            {
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Text)
                        raw = reader.Value;
                    else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "v")
                        break;
                }
            }
        }

        string? value;
        if (inlineValue is not null)
        {
            value = inlineValue;
        }
        else if (type == "s" && raw is not null &&
                 int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
                 sharedStrings is not null && index >= 0 && index < sharedStrings.Length)
        {
            value = sharedStrings[index];
        }
        else
        {
            value = raw;
        }

        return (column, value);
    }

    /// <summary>Consumes an &lt;is&gt; inline-string element through its close tag; returns the &lt;t&gt; text.</summary>
    private static string ReadInlineText(XmlReader reader)
    {
        var buffer = new StringBuilder();
        var inT = false;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "is")
                break;

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
                inT = true;
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "t")
                inT = false;
            else if (inT && reader.NodeType == XmlNodeType.Text)
                buffer.Append(reader.Value);
        }

        return buffer.ToString();
    }

    /// <summary>"B4" → 3 (0-based); a missing reference degrades to the current ordinal via -1 guard.</summary>
    private static int ColumnNumberFromReference(string reference)
    {
        var column = 0;
        foreach (var ch in reference)
        {
            if (ch >= 'A' && ch <= 'Z')
                column = column * 26 + (ch - 'A' + 1);
            else if (ch >= 'a' && ch <= 'z')
                column = column * 26 + (ch - 'a' + 1);
            else
                break;
        }
        return Math.Max(0, column - 1);
    }

    // ── Cell values ───────────────────────────────────────────────────────

    /// <summary>
    /// Parse a date cell: OFLC files carry strings ("2/25/2026", "2026-02-25")
    /// or Excel serial numbers. Serial range 20000..80000 → 1954..2119.
    /// </summary>
    public static DateTime? ParseCellDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = text.Trim();

        if (DateTime.TryParseExact(text, new[] { "yyyy-MM-dd", "M/d/yyyy", "M/d/yyyy h:mmtt", "M/d/yyyy h:mm:ss tt" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed.Date;

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            return parsed.Date;

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var serial) &&
            serial is >= 20000 and <= 80000)
            return BaseSerialDate.AddDays(serial).Date;

        return null;
    }

    private static readonly DateTime BaseSerialDate = new(1899, 12, 30); // Excel 1900 system

    // ── Write ────────────────────────────────────────────────────────────

    private void WriteCache(string sourceUrl, DateTime cutoff, IReadOnlyList<PermFiling> filings)
    {
        var label = sourceUrl.Split('/').Last();
        var envelope = new
        {
            GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Source = $"U.S. DOL ETA OFLC PERM Disclosure Data ({label}; refreshed by Envoy PermIndexBuilder)",
            ReceivedFrom = cutoff.ToString("yyyy-MM-dd"),
            ColumnOrder = new[] { "Employer", "JobTitle", "State", "SocCode", "ReceivedDate" },
            Rows = filings.Select(f => new string?[]
            {
                f.Employer, f.JobTitle,
                f.State.Length > 0 ? f.State : null,
                f.SocCode.Length > 0 ? f.SocCode : null,
                f.ReceivedDate.ToString("yyyy-MM-dd")
            }).ToList()
        };

        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        var tmp = CachePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, CachePath, overwrite: true);
    }
}
