using System.Text.Json;
using Envoy.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Envoy.Core.Services;

public class ElementLocatorService : IElementLocator
{
    private readonly IBrowserQuery _browser;
    private readonly EnvoySettings _settings;
    private readonly RelocationLogger _relocationLogger;
    private readonly ILogger<ElementLocatorService> _log;

    private static readonly string DomSnapshotScript = @"
(function() {
  var FORM_TAGS = ['input','select','textarea','button','a'];
  var candidates = [];
  function getAncestorChain(el) {
    var chain = [];
    var parent = el.parentElement;
    var depth = 0;
    while (parent && parent !== document.body && parent !== document.documentElement && depth < 10) {
      var sel = parent.tagName.toLowerCase();
      if (parent.id) sel += '#' + parent.id;
      else if (parent.className && typeof parent.className === 'string') {
        var firstClass = parent.className.trim().split(/\s+/)[0];
        if (firstClass) sel += '.' + firstClass;
      }
      chain.unshift(sel);
      parent = parent.parentElement;
      depth++;
    }
    return chain;
  }
  function getUniqueSelector(el) {
    if (el.id) return '#' + el.id;
    var parts = [];
    var cur = el;
    while (cur && cur !== document.body && cur !== document.documentElement) {
      var part = cur.tagName.toLowerCase();
      if (cur.id) { parts.unshift(part + '#' + cur.id); break; }
      else {
        var p = cur.parentElement;
        if (p) {
          var idx = 0, count = 0;
          for (var i = 0; i < p.children.length; i++) {
            if (p.children[i].tagName === cur.tagName) { count++; if (p.children[i] === cur) idx = count; }
          }
          if (count > 1) part += ':nth-of-type(' + idx + ')';
        }
        parts.unshift(part);
      }
      cur = cur.parentElement;
    }
    return parts.join(' > ');
  }
  function getLabelText(el) {
    if (el.id) {
      var label = document.querySelector('label[for=""' + el.id + '""]');
      if (label) return (label.textContent || '').trim();
    }
    if (el.getAttribute('aria-label')) return el.getAttribute('aria-label');
    var title = el.getAttribute('title');
    if (title) return title;
    var parentLabel = el.closest('label');
    if (parentLabel) {
      var text = (parentLabel.textContent || '').trim();
      if (text.length > 0 && text.length < 200) return text;
    }
    return '';
  }
  function walk(root) {
    for (var i = 0; i < root.children.length; i++) {
      var el = root.children[i];
      var tag = el.tagName.toLowerCase();
      if (FORM_TAGS.indexOf(tag) >= 0 || el.getAttribute('role') === 'button' || el.getAttribute('role') === 'link') {
        var ancestors = getAncestorChain(el);
        var siblingsBefore = [];
        var siblingsAfter = [];
        if (el.parentElement) {
          var children = el.parentElement.children;
          var found = false;
          for (var j = 0; j < children.length; j++) {
            var child = children[j];
            if (child === el) { found = true; continue; }
            var txt = (child.textContent || '').trim().slice(0,100);
            if (txt.length > 0) {
              if (!found && siblingsBefore.length < 3) siblingsBefore.push(txt);
              else if (found && siblingsAfter.length < 3) siblingsAfter.push(txt);
            }
          }
        }
        var positionIndex = 0;
        if (el.parentElement) {
          for (var j = 0; j < el.parentElement.children.length; j++) {
            if (el.parentElement.children[j] === el) break;
            if (el.parentElement.children[j].tagName === el.tagName) positionIndex++;
          }
        }
        var attrs = {};
        for (var j = 0; j < el.attributes.length; j++) {
          attrs[el.attributes[j].name] = el.attributes[j].value;
        }
        candidates.push({
          tag: tag,
          attributes: attrs,
          textContent: (el.textContent || '').trim().slice(0,200),
          labelText: getLabelText(el),
          ancestors: ancestors,
          siblingsBefore: siblingsBefore,
          siblingsAfter: siblingsAfter,
          positionIndex: positionIndex,
          cssSelector: getUniqueSelector(el)
        });
      }
      if (el.children.length > 0) walk(el);
    }
  }
  walk(document.body || document.documentElement);
  return JSON.stringify(candidates);
})()";

    public ElementLocatorService(
        IBrowserQuery browser,
        EnvoySettings settings,
        RelocationLogger relocationLogger,
        ILogger<ElementLocatorService> log)
    {
        _browser = browser;
        _settings = settings;
        _relocationLogger = relocationLogger;
        _log = log;
    }

    public async Task<LocateResult> LocateAsync(TemplateStep step, string templateId, CancellationToken ct = default)
    {
        var originalSelector = step.Selector;

        if (!string.IsNullOrEmpty(step.Selector))
        {
            var nodeId = await _browser.QuerySelectorAsync(step.Selector, ct);
            if (nodeId != null)
                return new LocateResult(nodeId, step.Selector, 1.0, false, originalSelector, null);
        }

        if (!string.IsNullOrEmpty(step.FallbackSelector))
        {
            var nodeId = await _browser.QuerySelectorAsync(step.FallbackSelector, ct);
            if (nodeId != null)
                return new LocateResult(nodeId, step.FallbackSelector, 0.9, false, originalSelector, null);
        }

        if (step.Fingerprint != null)
        {
            var result = await TryRelocateAsync(step.Fingerprint, originalSelector, step, templateId, ct);
            if (result != null) return result;
        }

        var failureReason = step.Fingerprint != null ? "low_confidence" : "no_fingerprint";
        return new LocateResult(null, null, 0, false, originalSelector, failureReason);
    }

    private async Task<LocateResult?> TryRelocateAsync(
        Fingerprint fingerprint,
        string? originalSelector,
        TemplateStep step,
        string templateId,
        CancellationToken ct)
    {
        try
        {
            var json = await _browser.EvaluateJsAsync(DomSnapshotScript, ct);
            if (string.IsNullOrEmpty(json)) return null;

            var candidates = JsonSerializer.Deserialize<List<DomCandidate>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (candidates == null || candidates.Count == 0) return null;

            var threshold = _settings.RelocationConfidenceThreshold;
            var best = DomScorer.FindBestMatch(fingerprint, candidates, threshold);

            if (best == null || !best.AboveThreshold)
            {
                var score = best?.Score ?? 0;
                _log.LogWarning(
                    "Fingerprint relocation for {Field} fell below threshold: {Score:F3} < {Threshold:F3}",
                    step.FieldId ?? step.Selector, score, threshold);
                return null;
            }

            var nodeId = await _browser.QuerySelectorAsync(best.CssSelector, ct);
            if (nodeId == null)
            {
                _log.LogWarning("Fingerprint-relocated selector '{Selector}' did not match any element", best.CssSelector);
                return null;
            }

            _log.LogInformation(
                "Fingerprint relocated {Field}: '{Original}' -> '{New}' (confidence: {Score:F3})",
                step.FieldId ?? step.Selector, originalSelector, best.CssSelector, best.Score);

            var result = new LocateResult(nodeId, best.CssSelector, best.Score, true, originalSelector, null);

            await _relocationLogger.LogAsync(new RelocationEntry(
                templateId,
                step.FieldId ?? step.Selector ?? "",
                originalSelector ?? "",
                best.CssSelector,
                fingerprint,
                best.Score,
                DateTimeOffset.Now));

            return result;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error during fingerprint relocation for {Field}", step.FieldId ?? step.Selector);
            return null;
        }
    }
}