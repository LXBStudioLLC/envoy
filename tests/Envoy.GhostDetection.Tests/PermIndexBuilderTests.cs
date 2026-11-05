using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Envoy.GhostDetection.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Envoy.GhostDetection.Tests;

/// <summary>
/// Offline tests for the PERM index builder. The "disclosure file" is a
/// synthetic multi-hundred-KB XLSX built in-memory (ZipArchive + worksheet
/// XML). Zero network calls: the stub HttpClient serves the synthetic bytes.
/// </summary>
public sealed class PermIndexBuilderTests : IDisposable
{
    private readonly string _cachePath;

    public PermIndexBuilderTests()
    {
        _cachePath = Path.Combine(Path.GetTempPath(), "envoy-perm-builder-tests", Guid.NewGuid().ToString("N"), "perm-index.json");
        PermIndex.LoadOverride = () => Array.Empty<PermFiling>();
        PermIndex.Replace(null);
    }

    public void Dispose()
    {
        PermIndex.LoadOverride = null;
        PermIndex.Replace(null);
        try { Directory.Delete(Path.GetDirectoryName(_cachePath)!, recursive: true); } catch { }
    }

    // ── Synthetic XLSX construction ──────────────────────────────────────

    private static string ColumnName(int index)
    {
        var name = "";
        index++;
        while (index > 0)
        {
            var part = (index - 1) % 26;
            name = (char)('A' + part) + name;
            index = (index - 1) / 26;
        }
        return name;
    }

    private static string ColumnRef(int column, int row1Based) => ColumnName(column) + row1Based;

    /// <summary>
    /// Build a worksheet row; null cells are simply omitted (matching real files).
    /// Strings are inline &lt;is&gt; cells; values starting with '#' are emitted as raw
    /// numeric cells (Excel dates as serials).
    /// </summary>
    private static byte[] BuildXlsx((string Header, int Column)[] headers, params string?[][] rows)
    {
        var maxRow = rows.Length + 1;
        var sb = new StringBuilder();
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

        sb.Append("<row r=\"1\">");
        foreach (var (header, column) in headers)
        {
            var text = new XElement("t", header).ToString();
            sb.Append($"<c r=\"{ColumnRef(column, 1)}\" t=\"inlineStr\"><is>{text}</is></c>");
        }
        sb.Append("</row>");

        for (var i = 0; i < rows.Length; i++)
        {
            var rowNumber = i + 2; // 1-based, header is row 1
            sb.Append($"<row r=\"{rowNumber}\">");
            for (var c = 0; c < rows[i].Length; c++)
            {
                var value = rows[i][c];
                if (value is null)
                    continue;

                var reference = ColumnRef(c, rowNumber);
                if (value.StartsWith('#'))
                {
                    var serial = value[1..];
                    sb.Append($"<c r=\"{reference}\"><v>{serial}</v></c>");
                }
                else
                {
                    var text = new XElement("t", value).ToString();
                    sb.Append($"<c r=\"{reference}\" t=\"inlineStr\"><is>{text}</is></c>");
                }
            }
            sb.Append("</row>");
        }

        sb.Append("</sheetData></worksheet>");

        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var sheet = zip.CreateEntry("xl/worksheets/sheet1.xml");
            using var writer = new StreamWriter(sheet.Open(), Encoding.UTF8);
            writer.Write(sb.ToString());
        }
        return ms.ToArray();
    }

    private static readonly (string Header, int Column)[] FullHeaders =
    {
        ("EMP_BUSINESS_NAME", 0), ("EMP_TRADE_NAME", 1), ("JOB_TITLE", 2),
        ("PWD_SOC_TITLE", 3), ("PRIMARY_WORKSITE_STATE", 4), ("PWD_SOC_CODE", 5), ("RECEIVED_DATE", 6)
    };

    /// <summary>Rows for the happy-path workbook: past-cutoff rows, duplicates, fresh rows.</summary>
    private static List<string?[]> BuildRowSet(int wannabeCount, DateTime cutoff)
    {
        var rows = new List<string?[]>();
        var fresh = cutoff.AddDays(30).ToString("yyyy-MM-dd");
        var newer = cutoff.AddDays(60).ToString("yyyy-MM-dd");
        var old = cutoff.AddDays(-10).ToString("yyyy-MM-dd");

        // A duplicate pair: same normalized employer/title/state, second fresher.
        rows.Add(new string?[] { "Acme Widgets, LLC", "Acme Widgets", "Senior Widget Engineer", "Senior Widget Dev", "WA", "11-9041.00", fresh });
        rows.Add(new string?[] { "Acme Widgets LLC", "Acme Widgets", "Senior Widget Engineer", "Senior Widget Dev", "WA", "11-9041.00", newer });

        // A too-old filing: filtered by the cutoff.
        rows.Add(new string?[] { "Museum Corp", "Museum Corp", "Curator II", "Curator", "NY", "25-4013.00", old });

        // Enough fresh rows to clear the sanity gate.
        while (rows.Count < wannabeCount)
        {
            var i = rows.Count;
            rows.Add(new string?[]
            {
                $"Company {i} Inc.", "Company " + i, $"Role {i}", $"Role Title {i}",
                i % 2 == 0 ? "OR" : "TX", $"15-{1000 + i:0000}.00", fresh
            });
        }
        return rows;
    }

    // ── Candidate URLs ───────────────────────────────────────────────────

    [Fact]
    public void CandidateSourceUrls_FiscalYearOrder()
    {
        var urls = PermIndexBuilder.CandidateSourceUrls(new DateTime(2026, 11, 5));

        Assert.Equal("https://www.dol.gov/media/PERM_Disclosure_Data_FY2027_Q1.xlsx", urls[0]);
        Assert.Contains("https://www.dol.gov/media/PERM_Disclosure_Data_FY2026_Q3.xlsx", urls); // the proven live seed URL
        Assert.Equal(urls.Count, urls.Distinct().Count());
    }

    [Fact]
    public void CandidateSourceUrls_MidFiscalYear()
    {
        var urls = PermIndexBuilder.CandidateSourceUrls(new DateTime(2026, 3, 1)); // Feb–Mar = Q2

        Assert.Equal("https://www.dol.gov/media/PERM_Disclosure_Data_FY2026_Q2.xlsx", urls[0]);
        Assert.Equal("https://www.dol.gov/media/PERM_Disclosure_Data_FY2026_Q1.xlsx", urls[1]);
        Assert.Equal("https://www.dol.gov/media/PERM_Disclosure_Data_FY2025_Q4.xlsx", urls[2]);
    }

    // ── Cell date parsing ────────────────────────────────────────────────

    [Fact]
    public void ParseCellDate_StringAndSerialForms()
    {
        Assert.Equal(new DateTime(2026, 2, 25), PermIndexBuilder.ParseCellDate("2026-02-25"));
        Assert.Equal(new DateTime(2025, 3, 24), PermIndexBuilder.ParseCellDate("3/24/2025"));

        var serial = (int)(new DateTime(2026, 2, 25) - new DateTime(1899, 12, 30)).TotalDays;
        Assert.Equal(new DateTime(2026, 2, 25), PermIndexBuilder.ParseCellDate(serial.ToString()));

        Assert.Null(PermIndexBuilder.ParseCellDate("pending"));
        Assert.Null(PermIndexBuilder.ParseCellDate(""));
        Assert.Null(PermIndexBuilder.ParseCellDate(null));
    }

    // ── Shrink ───────────────────────────────────────────────────────────

    [Fact]
    public void Shrink_HappyPath_FiltersDedupesAndKeepsRicherRow()
    {
        var cutoff = new DateTime(2025, 11, 5);
        var rows = BuildRowSet(510, cutoff);
        var xlsx = BuildXlsx(FullHeaders, rows.ToArray());

        var (shrink, error) = PermIndexBuilder.Shrink(xlsx, cutoff);

        Assert.Null(error);
        Assert.True(shrink.Filings.Count >= 500, $"got {shrink.Filings.Count}");

        // The duplicate pair collapses to the fresher filing.
        var acmes = shrink.Filings.Where(f => f.Employer.Equals("Acme Widgets LLC", StringComparison.Ordinal)).ToList();
        Assert.Single(acmes);
        Assert.Equal(cutoff.AddDays(60), acmes[0].ReceivedDate);
        Assert.DoesNotContain(shrink.Filings, f => f.ReceivedDate < cutoff);
        Assert.Contains(shrink.Filings, f => f.State == "NYSE" || f.State == "OR" || f.State == "TX" || f.State == "WA");
        Assert.Equal("11-9041.00", shrink.Filings.First(f => f.Employer == "Acme Widgets LLC").SocCode);
    }

    [Fact]
    public void Shrink_MissingColumns_FailsLoudly()
    {
        var xlsx = BuildXlsx(new[] { ("COMPANY", 0), ("POSITION", 1) }, new[] { "Acme", "Engineer" });

        var (_, error) = PermIndexBuilder.Shrink(xlsx, DateTime.UtcNow.AddMonths(-12));

        Assert.NotNull(error);
        Assert.Contains("required columns", error);
    }

    [Fact]
    public void Shrink_TooFewRows_SanityGateRefuses()
    {
        var cutoff = DateTime.UtcNow.AddMonths(-12);
        var rows = BuildRowSet(0, cutoff); // only the duplicate/old rows, far below the gate
        var xlsx = BuildXlsx(FullHeaders, rows.ToArray());

        var (_, error) = PermIndexBuilder.Shrink(xlsx, cutoff);

        Assert.NotNull(error);
        Assert.Contains("sanity gate", error);
    }

    [Fact]
    public void Shrink_SerialDateCells_Work()
    {
        var cutoff = new DateTime(2025, 11, 5);
        var serialRow = new[]
        {
            "Serial Corp", null, "Principal Analyst", null, "FL", null,
            "#" + ((int)(cutoff.AddDays(1) - new DateTime(1899, 12, 30)).TotalDays)
        };

        // 500 identical-ish rows with the serial date to clear the gate.
        var rows = BuildRowSet(501, cutoff.AddDays(2));
        rows[0] = serialRow;
        var xlsx = BuildXlsx(FullHeaders, rows.ToArray());

        var (shrink, error) = PermIndexBuilder.Shrink(xlsx, cutoff);

        Assert.Null(error);
        Assert.Contains(shrink.Filings, f => f.Employer == "Serial Corp" && f.ReceivedDate == cutoff.AddDays(1));
    }

    // ── End-to-end build with a stub HttpClient ──────────────────────────

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public readonly List<string> Urls = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(_responder(request));
        }

        public static HttpResponseMessage Bytes(byte[] data, HttpStatusCode code = HttpStatusCode.OK) =>
            new(code) { Content = new ByteArrayContent(data) };
    }

    [Fact]
    public async Task BuildAsync_EndToEnd_WritesCacheFreshEnvelopeAndSwapsInMemoryIndex()
    {
        var cutoff = DateTime.UtcNow.AddMonths(-12);
        var xlsx = BuildXlsx(FullHeaders, BuildRowSet(520, cutoff).ToArray());
        var handler = new StubHandler(_ => StubHandler.Bytes(xlsx));

        // This test needs the real load path (no override) to observe the
        // in-memory swap; it manages the hook itself.
        PermIndex.LoadOverride = null;
        PermIndex.Replace(null);
        try
        {
            var builder = new PermIndexBuilder(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) }) { CachePath = _cachePath };
            var result = await builder.BuildAsync();

            Assert.True(result.Success, result.Error);
            Assert.Contains("PERM", result.Summary);
            Assert.True(result.Total >= BuildRowSet(0, cutoff).Count() - 2, $"got {result.Total}");

            // Requested a dol.gov candidate with the plain UA (not blocked-UA).
            Assert.Contains(handler.Urls, u => u.Contains("dol.gov"));
            Assert.Equal(result.SourceUrl, handler.Urls[0]);

            // Cache file written with the envelope/rows the loader understands.
            var json = File.ReadAllText(_cachePath);
            var envelope = JsonSerializer.Deserialize<JsonElement>(json);
            Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), envelope.GetProperty("GeneratedAt").GetString());
            var rows = envelope.GetProperty("Rows").EnumerateArray().ToList();
            Assert.Equal(result.Total, rows.Count);

            // In-memory index was swapped: a live load sees the fresh rows.
            var live = PermIndex.Load();
            Assert.Equal(result.Total, live.Count);
            Assert.Contains(live, f => f.Employer == "Acme Widgets LLC");
        }
        finally
        {
            PermIndex.LoadOverride = () => Array.Empty<PermFiling>();
        }
    }

    [Fact]
    public async Task BuildAsync_AllSourcesRefused_FailsGracefully()
    {
        var handler = new StubHandler(_ => StubHandler.Bytes(Array.Empty<byte>(), HttpStatusCode.Forbidden));

        var builder = new PermIndexBuilder(new HttpClient(handler)) { CachePath = _cachePath };
        var result = await builder.BuildAsync();

        Assert.False(result.Success);
        Assert.Contains("No candidate disclosure file", result.Error);
        // Every candidate was tried exactly once — no escalation loop.
        Assert.Equal(5, handler.Urls.Count);
    }

    [Fact]
    public async Task BuildAsync_MalformedWorkbook_FailsGracefully()
    {
        var garment = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00 }; // truncated zip signature
        var handler = new StubHandler(_ => StubHandler.Bytes(garment));

        var builder = new PermIndexBuilder(new HttpClient(handler)) { CachePath = _cachePath };
        var result = await builder.BuildAsync();

        Assert.False(result.Success);
        Assert.NotEmpty(result.Error!);
    }

    // ── DI ───────────────────────────────────────────────────────────────

    [Fact]
    public void Builder_RegistersAsTypedClient_AndResolves()
    {
        var services = new ServiceCollection();
        services.AddEnvoyGhostDetection();

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<PermIndexBuilder>());
    }
}
