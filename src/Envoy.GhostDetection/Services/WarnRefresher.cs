using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Envoy.GhostDetection.Services;

/// <summary>Outcome of a WARN dataset refresh attempt (never throws).</summary>
public sealed class WarnRefreshResult
{
    public bool Success { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string? Error { get; init; }
    /// <summary>Rows added over what was already in the dataset.</summary>
    public int Added { get; init; }
    /// <summary>Rows in the dataset after the refresh.</summary>
    public int Total { get; init; }
    /// <summary>Per-source notes (partial failures, blocks).</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Refreshes the local WARN Act notice dataset that <c>HiringFreezeSignal</c>
/// consumes. Fetches public, unauthenticated state-agency WARN pages, parses
/// them into <see cref="WarnNotice"/> records, merges them with the existing
/// dataset, and writes the result to the user cache
/// (<c>%LOCALAPPDATA%\Envoy\warn-notices.json</c>) — the first path
/// <see cref="WarnDataset"/> loads, with the bundled seed as fallback.
///
/// <para>This is a background maintenance step, deliberately NOT part of any
/// signal's <c>EvaluateAsync</c>: per-posting evaluation stays local and
/// network-free. Sources are public government pages; nothing here logs in,
/// bypasses authentication, or defeats a bot-check — a CAPTCHA or block page
/// is surfaced and the refresh stops.</para>
/// </summary>
public sealed class WarnRefresher
{
    private const string WaUrl = "https://fortress.wa.gov/esd/file/WARN/Public/SearchWARN.aspx";
    private const string NyUrl = "https://dol.ny.gov/warn-notices";

    private readonly HttpClient _http;
    private readonly ILogger<WarnRefresher>? _logger;

    /// <summary>
    /// Where the refreshed dataset is written. Defaults to the user cache
    /// that <see cref="WarnDataset"/> reads first; tests redirect it.
    /// </summary>
    public string CachePath { get; init; } = WarnDataset.CachePath;

    public WarnRefresher(HttpClient http, ILogger<WarnRefresher>? logger = null)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Run the full refresh: both sources, merge, atomic write. Returns a
    /// result describing what happened; any failure is reported, not thrown.
    /// </summary>
    public async Task<WarnRefreshResult> RefreshAsync(
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var notes = new List<string>();
        var fetched = new List<WarnNotice>();

        try
        {
            var (waNotices, waError) = await FetchWashingtonAsync(progress, cancellationToken);
            if (waError is null)
                fetched.AddRange(waNotices);
            else
                notes.Add($"Washington source unavailable ({waError})");

            var (nyNotices, nyError) = await FetchNewYorkAsync(progress, cancellationToken);
            if (nyError is null)
                fetched.AddRange(nyNotices);
            else
                notes.Add($"New York source unavailable ({nyError})");

            if (waError is not null && nyError is not null)
                return new WarnRefreshResult
                {
                    Success = false,
                    Notes = notes,
                    Error = "Both sources failed: " + string.Join("; ", notes)
                };

            var existing = WarnDataset.Load();
            var merged = Merge(existing, fetched);
            WriteCache(merged);
            WarnDataset.Replace(merged); // live process scores with fresh data immediately

            var added = merged.Count - existing.Count;
            var byState = merged
                .GroupBy(n => n.State)
                .Select(g => $"{g.Key} {g.Count()}")
                .ToArray();

            var summary = added > 0
                ? $"Refreshed: +{added} new notice{(added == 1 ? "" : "s")} ({string.Join(", ", byState)}), {merged.Count} in the dataset."
                : $"Refreshed: no new notices ({string.Join(", ", byState)}; {merged.Count} total).";

            _logger?.LogInformation("WARN refresh: fetched {Fetched}, merged total {Total} (+{Added})", fetched.Count, merged.Count, added);
            return new WarnRefreshResult { Success = true, Summary = summary, Added = Math.Max(0, added), Total = merged.Count, Notes = notes };
        }
        catch (OperationCanceledException)
        {
            return new WarnRefreshResult { Success = false, Error = "Refresh cancelled." };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "WARN refresh failed");
            return new WarnRefreshResult { Success = false, Error = ex.Message, Notes = notes };
        }
    }

    // ── Source: Washington (ASP.NET WebForms grid; proven postback flow) ──

    private async Task<(List<WarnNotice> Notices, string? Error)> FetchWashingtonAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var response = await _http.GetAsync(WaUrl, ct);
        if (!response.IsSuccessStatusCode)
            return (new List<WarnNotice>(), $"HTTP {(int)response.StatusCode} on first page");

        var html = await response.Content.ReadAsStringAsync(ct);
        if (LooksLikeBlock(html))
            return (new List<WarnNotice>(), "the site served a bot-check/CAPTCHA page; not attempting to bypass it");

        var notices = ParseWaHtml(html, out var maxPage);
        progress?.Report($"Washington WARN: page 1, {notices.Count} notices");

        for (var page = 2; page <= maxPage; page++)
        {
            ct.ThrowIfCancellationRequested();

            var postback = BuildWaPostBack(html, page);
            using var content = new StringContent(postback, Encoding.ASCII, "application/x-www-form-urlencoded");
            using var pageResponse = await _http.PostAsync(WaUrl, content, ct);
            if (!pageResponse.IsSuccessStatusCode)
                break; // keep the pages collected so far; the merge still runs

            html = await pageResponse.Content.ReadAsStringAsync(ct);
            if (LooksLikeBlock(html))
                return (notices, "the site served a bot-check/CAPTCHA page mid-paging; not attempting to bypass it");

            var rows = ParseWaHtml(html, out _);
            if (rows.Count == 0)
                break;

            notices.AddRange(rows);
            progress?.Report($"Washington WARN: page {page}/{maxPage}, {notices.Count} notices");
        }

        return (notices, null);
    }

    /// <summary>Builds the WebForms postback body that pages the WA grid.</summary>
    private static string BuildWaPostBack(string html, int page)
    {
        var viewState = Regex.Match(html, "id=\"__VIEWSTATE\" value=\"([^\"]*)\"").Groups[1].Value;
        var eventValidation = Regex.Match(html, "id=\"__EVENTVALIDATION\" value=\"([^\"]*)\"").Groups[1].Value;
        var generator = Regex.Match(html, "id=\"__VIEWSTATEGENERATOR\" value=\"([^\"]*)\"").Groups[1].Value;

        return string.Join('&', new[]
        {
            "__EVENTTARGET=ucPSW%24gvMain",
            $"__EVENTARGUMENT=Page%24{page}",
            "__LASTFOCUS=",
            "__VIEWSTATE=" + Uri.EscapeDataString(viewState),
            "__VIEWSTATEGENERATOR=" + generator,
            "__VIEWSTATEENCRYPTED=",
            "__EVENTVALIDATION=" + Uri.EscapeDataString(eventValidation),
            "ucPSW%24txtSearch="
        });
    }

    // ── Source: New York (single public HTML table) ──────────────────────

    private async Task<(List<WarnNotice> Notices, string? Error)> FetchNewYorkAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var response = await _http.GetAsync(NyUrl, ct);
        if (!response.IsSuccessStatusCode)
            return (new List<WarnNotice>(), $"HTTP {(int)response.StatusCode}");

        var html = await response.Content.ReadAsStringAsync(ct);
        if (LooksLikeBlock(html))
            return (new List<WarnNotice>(), "the site served a bot-check/CAPTCHA page; not attempting to bypass it");

        var notices = ParseNyHtml(html);

        // NY publishes company, labor-market region, and dates — no worker
        // counts. The region ("Capital", "Western") is not a city, so it is
        // deliberately not stored: Match()'s reference-city contains-check
        // could otherwise wrongly confirm a posting in a different state.
        progress?.Report($"New York WARN: {notices.Count} notices");
        return (notices, null);
    }

    // ── Parsers (internal static: offline unit tests feed them fixtures) ──

    /// <summary>
    /// Parses one page of the WA grid. <c>maxPage</c> reports the grid's
    /// highest pager page ("Page$N"), 0 when the grid fits one page.
    /// Public static: offline unit tests feed it the captured HTML fixtures.
    /// </summary>
    public static List<WarnNotice> ParseWaHtml(string html, out int maxPage)
    {
        maxPage = 0;
        var notices = new List<WarnNotice>();

        foreach (Match pageRef in Regex.Matches(html, @"Page\$(\d+)"))
            if (int.TryParse(pageRef.Groups[1].Value, out var p))
                maxPage = Math.Max(maxPage, p);

        foreach (Match rowMatch in Regex.Matches(html, @"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline))
        {
            var cells = Regex.Matches(rowMatch.Groups[1].Value, @"<t[dh][^>]*>(.*?)</t[dh]>", RegexOptions.Singleline)
                .Select(c => HtmlToText(c.Groups[1].Value))
                .ToArray();

            // WA grid rows carry 8 cells (7 columns + a grid nuisance cell);
            // pager digit-only rows and the header repeat are dropped.
            if (cells.Length < 8 || cells[0] == "Company" || Regex.IsMatch(cells[0], @"^\d+$"))
                continue;

            if (!TryParseWaDate(cells[6], out var noticeDate))
                continue;

            int.TryParse(Regex.Replace(cells[3], "[^0-9]", string.Empty), out var workers);

            notices.Add(new WarnNotice
            {
                Employer = Squash(cells[0]),
                State = "WA",
                Location = string.IsNullOrWhiteSpace(cells[1]) ? null : Squash(cells[1]),
                NoticeDate = noticeDate,
                AffectedCount = workers
            });
        }

        return notices;
    }

    /// <summary>Parses the NY DOL WARN table (Company Name | Region | Date Posted | Notice Dated). Public static for offline tests.</summary>
    public static List<WarnNotice> ParseNyHtml(string html)
    {
        var notices = new List<WarnNotice>();
        var table = Regex.Match(html, @"<table[^>]*>.*?</table>", RegexOptions.Singleline).Value;

        foreach (Match rowMatch in Regex.Matches(table, @"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline))
        {
            var cells = Regex.Matches(rowMatch.Groups[1].Value, @"<t[dh][^>]*>(.*?)</t[dh]>", RegexOptions.Singleline)
                .Select(c => HtmlToText(c.Groups[1].Value))
                .ToArray();

            if (cells.Length < 3 || cells[0] == "Company Name" || string.IsNullOrWhiteSpace(cells[0]))
                continue;

            if (!TryParseFirstDate(cells[2], out var posted))
                continue;

            notices.Add(new WarnNotice
            {
                Employer = Squash(cells[0]),
                State = "NY",
                Location = null, // region labels are not cities; see FetchNewYorkAsync
                NoticeDate = posted,
                AffectedCount = 0
            });
        }

        return notices;
    }

    // ── Merge + write ────────────────────────────────────────────────────

    /// <summary>
    /// Union of existing and fetched records, deduped on
    /// (state, normalized employer, notice date). Records the sources no
    /// longer list are kept — a refresh only adds, it never erases history.
    /// </summary>
    public static List<WarnNotice> Merge(IReadOnlyList<WarnNotice> existing, IReadOnlyList<WarnNotice> fetched)
    {
        var result = new Dictionary<string, WarnNotice>(StringComparer.OrdinalIgnoreCase);

        foreach (var notice in existing.Concat(fetched))
        {
            if (string.IsNullOrWhiteSpace(notice.Employer) || notice.NoticeDate == default)
                continue;

            var key = $"{notice.State.Trim().ToUpperInvariant()}|{EmployerNormalizer.Normalize(notice.Employer)}|{notice.NoticeDate:yyyyMMdd}";

            if (!result.TryGetValue(key, out var current))
            {
                result[key] = notice;
                continue;
            }

            // The same notice published twice: keep the richer row.
            result[key] = notice.AffectedCount > current.AffectedCount ? notice : current;
        }

        return result.Values
            .OrderByDescending(n => n.NoticeDate)
            .ThenBy(n => n.Employer, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void WriteCache(IReadOnlyList<WarnNotice> merged)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        var tmp = CachePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(merged, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, CachePath, overwrite: true);
    }

    // ── Bot-check surfacing (constraint #3: surface, never bypass) ───────

    private static bool LooksLikeBlock(string html)
    {
        if (string.IsNullOrEmpty(html))
            return true;

        // Deliberately narrow: only clear bot-check markers abort the refresh.
        return html.Contains("cf-mitigated:", StringComparison.OrdinalIgnoreCase) ||
               html.Contains("checking your browser", StringComparison.OrdinalIgnoreCase) ||
               html.Contains("verify you are human", StringComparison.OrdinalIgnoreCase) ||
               html.Contains("captcha", StringComparison.OrdinalIgnoreCase) ||
               html.Contains("g-recaptcha", StringComparison.OrdinalIgnoreCase) ||
               html.Contains("h-captcha", StringComparison.OrdinalIgnoreCase);
    }

    // ── Small helpers ────────────────────────────────────────────────────

    public static bool TryParseWaDate(string text, out DateTime date)
    {
        date = default;
        text = text.Trim();
        if (text.Length == 0)
            return false;

        // WA publishes "M/d/yyyy h:mm:ss tt" (or bare "M/d/yyyy"); take the date prefix.
        var space = text.IndexOf(' ');
        var candidate = space > 0 ? text[..space] : text;
        return DateTime.TryParseExact(candidate, "M/d/yyyy", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);
    }

    public static bool TryParseFirstDate(string text, out DateTime date)
    {
        date = default;
        var m = Regex.Match(text, @"(\d{1,2})/(\d{1,2})/(\d{4})");
        if (!m.Success)
            return false;

        if (!int.TryParse(m.Groups[1].Value, out var month) ||
            !int.TryParse(m.Groups[2].Value, out var day) ||
            !int.TryParse(m.Groups[3].Value, out var year))
            return false;

        try { date = new DateTime(year, month, day); return true; }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static string HtmlToText(string cell) =>
        Squash(Regex.Replace(cell, "<[^>]+>", " ")
            .Replace("&nbsp;", " ")
            .Replace("&amp;", "&"));

    private static string Squash(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts).Trim();
    }
}
