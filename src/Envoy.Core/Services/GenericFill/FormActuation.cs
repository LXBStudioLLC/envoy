using System.Text.Json;

namespace Envoy.Core.Services;

/// <summary>Enumerates visible form widgets of the current page (APP-09).</summary>
public interface IFormFieldEnumerator
{
    Task<List<FormFieldInfo>> EnumerateAsync(CancellationToken ct = default);
}

/// <summary>CDP-backed enumerator: runs <see cref="FormFieldsScript.Enumerate"/> and parses the result.</summary>
public sealed class CdpFormFieldEnumerator : IFormFieldEnumerator
{
    private readonly IBrowserQuery _browser;

    public CdpFormFieldEnumerator(IBrowserQuery browser)
    {
        _browser = browser;
    }

    public async Task<List<FormFieldInfo>> EnumerateAsync(CancellationToken ct = default)
    {
        var json = await _browser.EvaluateJsAsync(FormFieldsScript.Enumerate, ct);

        try
        {
            var raw = JsonSerializer.Deserialize<List<RawField>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (raw is null) return new List<FormFieldInfo>();

            return raw.Select((r, i) => new FormFieldInfo(
                Index: i,
                Type: (r.Type ?? "text").ToLowerInvariant(),
                Label: r.Label ?? string.Empty,
                Selector: r.Selector ?? string.Empty,
                Required: r.Required,
                Options: r.Options ?? Array.Empty<string>(),
                CurrentValue: r.CurrentValue ?? string.Empty))
                .Where(f => !string.IsNullOrWhiteSpace(f.Selector))
                .ToList();
        }
        catch (JsonException)
        {
            return new List<FormFieldInfo>(); // enumeration failed → generic fill honestly does nothing
        }
    }

    private sealed record RawField(
        string? Type, string? Label, string? Selector, bool Required, string[]? Options, string? CurrentValue);
}

/// <summary>Per-field DOM actions used by the generic fill (APP-09).</summary>
public interface IFormFieldActuator
{
    /// <summary>Types into a text-like element (framework-safe value-semantics; keys stay Stealth's job).</summary>
    Task<bool> FillTextAsync(string selector, string value, CancellationToken ct = default);

    /// <summary>Picks the matching option (exact match first, then contained); false when nothing matches.</summary>
    Task<bool> SelectOptionAsync(string selector, string optionText, CancellationToken ct = default);

    /// <summary>Sets a checkbox state.</summary>
    Task<bool> SetCheckedAsync(string selector, bool isChecked, CancellationToken ct = default);

    /// <summary>Attaches a resume file to a file input by selector.</summary>
    Task<bool> UploadFileAsync(string selector, string filePath, CancellationToken ct = default);

    /// <summary>True when an element matching the selector exists and is visible.</summary>
    Task<bool> ExistsAsync(string selector, CancellationToken ct = default);
}

/// <summary>
/// CDP-backed actuator. All typing goes through page-level JS with native
/// value setters + input/change events (works on framework-managed inputs),
/// deliberately separate from Stealth's keystroke emulation: the generic path
/// runs in Safe mode only and its speed is not a stealth signature.
/// </summary>
public sealed class CdpFormFieldActuator : IFormFieldActuator
{
    private readonly ICdpCommandExecutor _cdp;

    public CdpFormFieldActuator(ICdpCommandExecutor cdp)
    {
        _cdp = cdp;
    }

    private async Task<JsonElement?> Eval(string expression, string selector, CancellationToken ct)
    {
        var result = await _cdp.SendCommandAsync("Runtime.evaluate", new
        {
            expression = expression.Replace("{sel}", selector.Replace("'", "\\'")),
            returnByValue = true
        }, ct);

        return result.TryGetProperty("result", out var r) && r.TryGetProperty("value", out var v)
            ? v
            : null;
    }

    public async Task<bool> FillTextAsync(string selector, string value, CancellationToken ct = default)
    {
        return await Eval(@"
(() => {
  const el = document.querySelector('{sel}');
  if (!el) return false;
  const proto = Object.getPrototypeOf(el);
  const desc = Object.getOwnPropertyDescriptor(proto, 'value');
  if (desc && desc.set) desc.set.call(el, '{vval}');
  else el.value = '{vval}';
  el.dispatchEvent(new Event('input', { bubbles: true }));
  el.dispatchEvent(new Event('change', { bubbles: true }));
  return true;
})()".Replace("{vval}", value.Replace("\\", "\\\\").Replace("'", "\\'")), selector, ct) is JsonElement v && v.ValueKind == JsonValueKind.True;
    }

    public async Task<bool> SelectOptionAsync(string selector, string optionText, CancellationToken ct = default)
    {
        return await Eval(@"
(() => {
  const el = document.querySelector('{sel}');
  if (!el || el.tagName !== 'SELECT') return false;
  const opts = [...el.options].filter((o) => o.disabled === false);
  let chosen = opts.find((o) => (o.text || '').trim().toLowerCase() === '{vval}'.trim().toLowerCase());
  if (!chosen) chosen = opts.find((o) => (o.text || '').trim().toLowerCase().includes('{vval}'.trim().toLowerCase()));
  if (!chosen && opts.length > 1) { /* still refuse: ambiguity beats a wrong answer */ chosen = null; }
  if (!chosen) return false;
  const desc = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(el), 'value');
  if (desc && desc.set) desc.set.call(el, chosen.value);
  else el.value = chosen.value;
  el.dispatchEvent(new Event('input', { bubbles: true }));
  el.dispatchEvent(new Event('change', { bubbles: true }));
  return true;
})()".Replace("{vval}", optionText.Replace("\\", "\\\\").Replace("'", "\\'")), selector, ct) is JsonElement v && v.ValueKind == JsonValueKind.True;
    }

    public async Task<bool> SetCheckedAsync(string selector, bool isChecked, CancellationToken ct = default)
    {
        return await Eval(@"
(() => {
  const el = document.querySelector('{sel}');
  if (!el || (el.type !== 'checkbox' && el.type !== 'radio')) return false;
  el.checked = 'vbool' === 'true';
  el.dispatchEvent(new Event('change', { bubbles: true }));
  return true;
})()".Replace("vbool", isChecked ? "true" : "false"), selector, ct) is JsonElement v && v.ValueKind == JsonValueKind.True;
    }

    public async Task<bool> UploadFileAsync(string selector, string filePath, CancellationToken ct = default)
    {
        try
        {
            var doc = await _cdp.SendCommandAsync("DOM.getDocument", new { depth = 0 }, ct);
            var nodeId = doc.TryGetProperty("root", out var root) && root.TryGetProperty("nodeId", out var rid)
                ? rid.GetInt32()
                : 0;
            if (nodeId == 0) return false;

            var node = await _cdp.SendCommandAsync("DOM.querySelector", new { nodeId, selector }, ct);
            if (!node.TryGetProperty("nodeId", out var found) || found.GetInt32() == 0)
                return false;

            await _cdp.SendCommandAsync("DOM.setFileInputFiles", new { nodeId = found.GetInt32(), files = new[] { filePath } }, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ExistsAsync(string selector, CancellationToken ct = default)
    {
        return await Eval("!!document.querySelector('{sel}')", selector, ct) is JsonElement v && v.ValueKind == JsonValueKind.True;
    }
}
