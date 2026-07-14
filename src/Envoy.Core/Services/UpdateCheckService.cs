using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Envoy.Core.Services;

public sealed record UpdateInfo(string LatestVersion, string ReleaseUrl);

public interface IUpdateCheckService
{
    Task<UpdateInfo?> CheckForUpdateAsync(string currentVersion, CancellationToken ct = default);
}

/// <summary>
/// Asks the public GitHub releases API whether a newer release exists. Read-only,
/// unauthenticated, one call per app launch. Never throws: any failure (offline,
/// rate-limited, malformed response) means "no update known" and returns null.
/// </summary>
public sealed class UpdateCheckService : IUpdateCheckService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/LXBStudioLLC/envoy/releases/latest";
    private const string ReleasesFallbackUrl = "https://github.com/LXBStudioLLC/envoy/releases/latest";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly ILogger<UpdateCheckService> _log;

    public UpdateCheckService(HttpClient http, ILogger<UpdateCheckService> log)
    {
        _http = http;
        _log = log;
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync(string currentVersion, CancellationToken ct = default)
    {
        if (!Version.TryParse(Normalize(currentVersion), out var current))
            return null;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(RequestTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.UserAgent.ParseAdd("Envoy/1.0 (+https://github.com/LXBStudioLLC/envoy)");

            using var response = await _http.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogInformation("Update check skipped: GitHub returned {Status}", (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);

            var tag = doc.RootElement.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            var latestText = Normalize(tag);
            if (!Version.TryParse(latestText, out var latest) || latest <= current)
                return null;

            // Only ever hand the UI a URL inside our own repo; anything else falls back
            // to the canonical releases page.
            var url = doc.RootElement.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(url) ||
                !url.StartsWith("https://github.com/LXBStudioLLC/envoy/", StringComparison.OrdinalIgnoreCase))
            {
                url = ReleasesFallbackUrl;
            }

            return new UpdateInfo(latestText, url);
        }
        catch (Exception ex)
        {
            _log.LogInformation("Update check failed: {Reason}", ex.Message);
            return null;
        }
    }

    private static string Normalize(string version)
    {
        var v = version.Trim();
        if (v.StartsWith('v') || v.StartsWith('V'))
            v = v[1..];
        var plus = v.IndexOf('+');
        if (plus >= 0)
            v = v[..plus];
        var dash = v.IndexOf('-');
        if (dash >= 0)
            v = v[..dash];
        return v;
    }
}
