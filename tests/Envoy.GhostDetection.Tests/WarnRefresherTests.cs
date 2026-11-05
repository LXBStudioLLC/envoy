using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Envoy.GhostDetection.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Envoy.GhostDetection.Tests;

/// <summary>
/// Offline tests for the WARN dataset refresher. Fixtures are real HTML
/// snapshots captured from the live public pages; the stub HttpClient serves
/// them in a fixed sequence — zero network calls.
/// </summary>
public sealed class WarnRefresherTests : IDisposable
{
    // Fixture convention: resolve from the source tree, exactly like the
    // signal test suites do (assemblyDir/../../../fixtures).
    private static readonly string FixtureDir = Path.Combine(
        Path.GetDirectoryName(typeof(WarnRefresherTests).Assembly.Location)!, "..", "..", "..", "fixtures");

    private readonly string _cachePath;

    public WarnRefresherTests()
    {
        _cachePath = Path.Combine(Path.GetTempPath(), "envoy-warn-refresher-tests", Guid.NewGuid().ToString("N"), "warn-notices.json");
        // Deterministic baseline: no bundled seed, empty in-memory dataset.
        WarnDataset.LoadOverride = () => Array.Empty<WarnNotice>();
        WarnDataset.Replace(null);
    }

    public void Dispose()
    {
        WarnDataset.LoadOverride = null;
        WarnDataset.Replace(null);
        try { Directory.Delete(Path.GetDirectoryName(_cachePath)!, recursive: true); } catch { }
    }

    private static string ReadFixture(string name) => File.ReadAllText(Path.Combine(FixtureDir, name));

    // ── WA parser ────────────────────────────────────────────────────────

    [Fact]
    public void WaPage1_ParsesNotices_AndPagerDepth()
    {
        var notices = WarnRefresher.ParseWaHtml(ReadFixture("warn-wa-page1.html"), out var maxPage);

        Assert.True(maxPage >= 11, $"expected pager to reach page 11, got {maxPage}");
        Assert.True(notices.Count >= 10, $"expected data rows, got {notices.Count}");
        Assert.All(notices, n =>
        {
            Assert.Equal("WA", n.State);
            Assert.True(n.NoticeDate >= new DateTime(2025, 1, 1), $"bad date {n.NoticeDate:O}");
        });
    }

    [Fact]
    public void WaPage1_KnownRow_ParsesCorrectly()
    {
        var notices = WarnRefresher.ParseWaHtml(ReadFixture("warn-wa-page1.html"), out _);

        var marriott = notices.Single(n => n.Employer == "Seattle Marriott Redmond");
        Assert.Equal(new DateTime(2026, 11, 4), marriott.NoticeDate);
        Assert.Equal(91, marriott.AffectedCount);
        Assert.Equal("Redmond", marriott.Location);

        var amazon = notices.Single(n => n.Employer == "Amazon");
        Assert.True(amazon.AffectedCount > 0);
        Assert.Contains("Remote", amazon.Location);
    }

    [Fact]
    public void WaPaging_FiltersHeaderAndPagerNoiseRows()
    {
        foreach (var fixture in new[] { "warn-wa-page1.html", "warn-wa-page2.html" })
        {
            var notices = WarnRefresher.ParseWaHtml(ReadFixture(fixture), out _);

            Assert.DoesNotContain(notices, n => n.Employer == "Company");
            Assert.DoesNotContain(notices, n => Regex.IsMatch(n.Employer, @"^\d+$"));
            Assert.All(notices, n => Assert.False(string.IsNullOrWhiteSpace(n.Employer)));
        }
    }

    [Fact]
    public void WaRows_WithUnparseableDates_AreDropped()
    {
        var html = """
            <table>
              <tr><td>Company</td><td>Location</td><td>Layoff Start Date</td><td># of Workers</td><td>Closure Layoff</td><td>Type of Layoff</td><td>Received Date</td><td>Notice</td></tr>
              <tr><td>Acme Widgets Inc.</td><td>Seattle</td><td>1/5/2027</td><td>91</td><td>Closure</td><td>Permanent</td><td></td><td></td></tr>
              <tr><td>Acme Widgets Inc.</td><td>Spokane</td><td>1/6/2027</td><td>12</td><td>Layoff</td><td>Permanent</td><td>11/2/2026</td><td></td></tr>
            </table>
            """;

        var notices = WarnRefresher.ParseWaHtml(html, out var maxPage);

        Assert.Equal(0, maxPage);
        var kept = Assert.Single(notices);
        Assert.Equal("Acme Widgets Inc.", kept.Employer); // suffix handling is EmployerNormalizer's job
        Assert.Equal(new DateTime(2026, 11, 2), kept.NoticeDate);
    }

    [Fact]
    public void WaDate_HandlesTimestampForm()
    {
        Assert.True(WarnRefresher.TryParseWaDate("11/4/2026", out var a));
        Assert.Equal(new DateTime(2026, 11, 4), a);

        Assert.True(WarnRefresher.TryParseWaDate("3/24/2025 9:00:00 AM", out var b));
        Assert.Equal(new DateTime(2025, 3, 24), b);

        Assert.False(WarnRefresher.TryParseWaDate("pending", out _));
    }

    // ── NY parser ────────────────────────────────────────────────────────

    [Fact]
    public void NyPage_ParsesNotices_WithStateAndNoCity()
    {
        var notices = WarnRefresher.ParseNyHtml(ReadFixture("warn-ny-current.html"));

        Assert.True(notices.Count >= 60, $"expected the full NY table, got {notices.Count}");
        Assert.All(notices, n =>
        {
            Assert.Equal("NY", n.State);
            Assert.Equal(0, n.AffectedCount);
            Assert.Null(n.Location); // region labels are not cities
        });

        var plug = notices.Single(n => n.Employer == "Plug Power, Inc.");
        Assert.Equal(new DateTime(2025, 3, 31), plug.NoticeDate);
    }

    [Fact]
    public void NyPage_DoesNotKeepHeaderRow()
    {
        var notices = WarnRefresher.ParseNyHtml(ReadFixture("warn-ny-current.html"));

        Assert.DoesNotContain(notices, n => n.Employer == "Company Name");
        Assert.Contains(notices, n => n.Employer.Contains('&')); // "&amp;" decoded
    }

    // ── Merge ────────────────────────────────────────────────────────────

    [Fact]
    public void Merge_AddsNew_KeepsExisting_DedupesByStateEmployerDate()
    {
        var existing = new[]
        {
            new WarnNotice { Employer = "Old Widgets LLC", State = "WA", NoticeDate = new DateTime(2026, 1, 5), AffectedCount = 10 },
            new WarnNotice { Employer = "Acme, Inc.", State = "WA", NoticeDate = new DateTime(2026, 2, 1), AffectedCount = 5 }
        };
        var fetched = new[]
        {
            // Same normalized employer + date as an existing row, richer count.
            new WarnNotice { Employer = "Acme Inc", State = "WA", NoticeDate = new DateTime(2026, 2, 1), AffectedCount = 7 },
            // Employer matches after suffix normalization, but a different date: kept.
            new WarnNotice { Employer = "Acme, Inc.", State = "WA", NoticeDate = new DateTime(2026, 9, 9), AffectedCount = 3 },
            // Different state, same employer + date: not a duplicate.
            new WarnNotice { Employer = "Old Widgets LLC", State = "NY", NoticeDate = new DateTime(2026, 1, 5), AffectedCount = 1 },
            // A genuinely new WA notice.
            new WarnNotice { Employer = "Brand New Corp", State = "WA", NoticeDate = new DateTime(2026, 11, 1), AffectedCount = 50 }
        };

        var merged = WarnRefresher.Merge(existing, fetched);

        Assert.Equal(5, merged.Count);
        // Richer row wins the dedupe slot.
        Assert.Equal(7, merged.Single(n => n.Employer.Contains("Acme") && n.NoticeDate == new DateTime(2026, 2, 1)).AffectedCount);
        Assert.DoesNotContain(merged, n => n.AffectedCount == 5);
        // Newest first ordering.
        Assert.Equal("Brand New Corp", merged[0].Employer);
        Assert.Equal(new DateTime(2026, 9, 9), merged[1].NoticeDate);
        // Refresh never erases history: the old row survives.
        Assert.Contains(merged, n => n.Employer == "Old Widgets LLC" && n.State == "WA");
    }

    [Fact]
    public void Merge_DropsRowsWithoutEmployerOrDate()
    {
        var merged = WarnRefresher.Merge(Array.Empty<WarnNotice>(), new[]
        {
            new WarnNotice { Employer = " ", State = "WA", NoticeDate = new DateTime(2026, 5, 5) },
            new WarnNotice { Employer = "No Date Corp", State = "WA", NoticeDate = default },
            new WarnNotice { Employer = "Good Corp", State = "WA", NoticeDate = new DateTime(2026, 5, 6) }
        });

        Assert.Equal("Good Corp", Assert.Single(merged).Employer);
    }

    // ── End-to-end refresh with a stub HttpClient ────────────────────────

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public readonly List<(string Method, string Url, string? Body)> Requests = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            Requests.Add((request.Method.Method, request.RequestUri!.ToString(), body));
            return Task.FromResult(_responder(request));
        }

        public static HttpResponseMessage Html(string text, HttpStatusCode code = HttpStatusCode.OK) =>
            new(code) { Content = new StringContent(text, System.Text.Encoding.UTF8, "text/html") };
    }

    [Fact]
    public async Task RefreshAsync_EndToEnd_FetchesPagesMergesAndWritesCache()
    {
        var waPage1 = ReadFixture("warn-wa-page1.html");
        var waPage2 = ReadFixture("warn-wa-page2.html");
        var ny = ReadFixture("warn-ny-current.html");
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.ToString().Contains("fortress.wa.gov"))
            {
                if (request.Method == HttpMethod.Post)
                    return StubHandler.Html(waPage2);
                return StubHandler.Html(waPage1);
            }
            return StubHandler.Html(ny);
        });

        var refresher = new WarnRefresher(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) }) { CachePath = _cachePath };
        var result = await refresher.RefreshAsync();

        Assert.True(result.Success, result.Error);
        Assert.True(result.Total > 0);

        // All pager pages were requested with the proven WebForms postback body.
        var posts = handler.Requests.Where(r => r.Method == "POST").ToList();
        Assert.True(posts.Count >= 10, $"expected the full WA paging loop, got {posts.Count}");
        Assert.All(posts, p => Assert.Contains("__EVENTTARGET=ucPSW%24gvMain", p.Body!));
        Assert.Contains(posts, p => p.Body!.Contains("__EVENTARGUMENT=Page%2411"));

        // Cache file written, valid JSON, loadable.
        Assert.True(File.Exists(_cachePath));
        var json = File.ReadAllText(_cachePath);
        var persisted = JsonSerializer.Deserialize<List<WarnNotice>>(json);
        Assert.NotNull(persisted);
        Assert.Equal(result.Total, persisted!.Count);
        Assert.All(persisted, n => Assert.True(n.NoticeDate >= new DateTime(2024, 1, 1)));

        // Both sources contributed.
        Assert.Contains(persisted, n => n.State == "WA");
        Assert.Contains(persisted, n => n.State == "NY");
    }

    [Fact]
    public async Task RefreshAsync_BotCheckPage_IsSurfacedNotBypassed()
    {
        var ny = ReadFixture("warn-ny-current.html");
        var handler = new StubHandler(request =>
            request.RequestUri!.ToString().Contains("fortress.wa.gov")
                ? StubHandler.Html("<html>Please verify you are human</html>")
                : StubHandler.Html(ny));

        var result = await new WarnRefresher(new HttpClient(handler)) { CachePath = _cachePath }.RefreshAsync();

        // NY proceeds; WA's bot-check is surfaced in the notes, never bypassed.
        Assert.True(result.Success);
        var note = Assert.Single(result.Notes);
        Assert.Contains("bot-check", note);
        Assert.Contains("Washington", note);
        // The blocked source was hit exactly once: no escalation, no retry storm.
        Assert.Equal(1, handler.Requests.Count(r => r.Url.Contains("fortress.wa.gov")));
    }

    [Fact]
    public async Task RefreshAsync_BothSourcesBlocked_FailsGracefully()
    {
        var handler = new StubHandler(_ => StubHandler.Html("<html>Access denied: captcha</html>", HttpStatusCode.Forbidden));

        var result = await new WarnRefresher(new HttpClient(handler)) { CachePath = _cachePath }.RefreshAsync();

        Assert.False(result.Success);
        Assert.Contains("Both sources failed", result.Error);
        Assert.Equal(2, result.Notes.Count);
    }

    [Fact]
    public async Task RefreshAsync_HttpFailure_DoesNotThrow()
    {
        var handler = new StubHandler(_ => StubHandler.Html("<html>server error</html>", HttpStatusCode.InternalServerError));

        var result = await new WarnRefresher(new HttpClient(handler)) { CachePath = _cachePath }.RefreshAsync();

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task RefreshAsync_PreCancelledToken_ReturnsCancelledResult()
    {
        var handler = new StubHandler(_ => throw new OperationCanceledException());

        var result = await new WarnRefresher(new HttpClient(handler)) { CachePath = _cachePath }
            .RefreshAsync(cancellationToken: new CancellationToken(canceled: true));

        Assert.False(result.Success);
        Assert.Equal("Refresh cancelled.", result.Error);
    }

    [Fact]
    public async Task RefreshAsync_MalformedHtml_PersistsEmptyResult_WithoutThrowing()
    {
        var handler = new StubHandler(_ => StubHandler.Html("<html><body>not a warn page</body></html>"));

        var result = await new WarnRefresher(new HttpClient(handler)) { CachePath = _cachePath }.RefreshAsync();

        Assert.True(result.Success);
        Assert.Equal(0, result.Total);
        Assert.Contains("no new notices", result.Summary);
    }

    // ── DI + dataset handoff ─────────────────────────────────────────────

    [Fact]
    public void Refresher_RegistersAsTypedClient_AndResolves()
    {
        var services = new ServiceCollection();
        services.AddEnvoyGhostDetection();

        using var provider = services.BuildServiceProvider();
        var refresher = provider.GetRequiredService<WarnRefresher>();

        Assert.NotNull(refresher);
    }

    [Fact]
    public async Task RefreshAsync_HandsMergedDatasetToTheLiveProcess()
    {
        // This test needs the real load path (no override), so it manages
        // the hook itself.
        WarnDataset.LoadOverride = null;
        WarnDataset.Replace(null);
        try
        {
            var html = """
                <table>
                  <tr><td>Company Name</td><td>Region</td><td>Date Posted</td><td>Notice Dated</td></tr>
                  <tr><td>Fresh Corp, LLC</td><td>New York City</td><td>6/1/2026</td><td>5/30/2026</td></tr>
                </table>
                """;
            var handler = new StubHandler(request =>
                request.RequestUri!.ToString().Contains("fortress.wa.gov")
                    ? StubHandler.Html("<html>no rows</html>")
                    : StubHandler.Html(html));
            var refresher = new WarnRefresher(new HttpClient(handler)) { CachePath = _cachePath };
            var result = await refresher.RefreshAsync();

            Assert.True(result.Success, result.Error);
            var live = WarnDataset.Load();
            Assert.Contains(live, n => n.Employer == "Fresh Corp, LLC");
            Assert.Equal(result.Total, live.Count);
        }
        finally
        {
            WarnDataset.LoadOverride = () => Array.Empty<WarnNotice>();
        }
    }
}
