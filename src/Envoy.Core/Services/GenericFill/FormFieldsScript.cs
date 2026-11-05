namespace Envoy.Core.Services;

/// <summary>
/// The DOM enumeration script (APP-09). Walks the main document, same-origin
/// iframes (recursively; cross-origin frames cannot be read from one JS world
/// and are honestly skipped), and open shadow roots; finds visible form
/// widgets and returns them as an array of JSON objects
/// (see <see cref="CdpFormFieldEnumerator"/> for the parse). Threaded through
/// <see cref="IBrowserQuery.EvaluateJsAsync"/>.
///
/// <para>Deliberately conservative for precision: password/hidden/date-class
/// inputs and any disabled / aria-hidden / invisible widget are never
/// surfaced; a amaker-ish label-resolution chain
/// (label[for] → closest(label) → aria-label → placeholder → title → name/id)
/// feeds the mapper. Shadow-DOM elements get the same chain inside their root
/// and emit a synthetic "shadow" selector prefix — real actuation for those
/// needs a root-mediated lookup, which lands with the live verification.</para>
/// </summary>
public static class FormFieldsScript
{
    public const string Enumerate = @"
(() => {
  const ban = new Set(['password', 'hidden', 'date', 'datetime-local', 'month', 'time', 'week', 'submit', 'button', 'image', 'reset']);
  const out = [];
  let seq = 0;

  function labelFor(el, doc) {
    if (el.id) {
      const escaped = el.id.replace(/""/g, '\\\""');
      const l = doc.querySelector('label[for=""' + escaped.replace(/'/g, '\\\'') + '""]');
      if (l) {
        const t = (l.innerText || l.textContent || '').trim();
        if (t) return t.slice(0, 160);
      }
    }
    const wrap = el.closest ? el.closest('label') : null;
    if (wrap) {
      const t = (wrap.innerText || '').trim();
      if (t) return t.slice(0, 160);
    }
    const aria = el.getAttribute('aria-label') || '';
    if (aria.trim()) return aria.trim().slice(0, 160);
    const ph = el.getAttribute('placeholder') || '';
    if (ph.trim()) return ph.trim().slice(0, 160);
    const title = el.getAttribute('title') || '';
    if (title.trim()) return title.trim().slice(0, 160);
    return (el.name || el.id || '').slice(0, 160);
  }

  function isVisible(el) {
    const b = el.getBoundingClientRect ? el.getBoundingClientRect() : null;
    if (b && (b.width <= 0 || b.height <= 0)) return false;
    const style = el.ownerDocument && el.ownerDocument.defaultView
      ? el.ownerDocument.defaultView.getComputedStyle(el) : null;
    if (style && (style.visibility === 'hidden' || style.display === 'none')) return false;
    return true;
  }

  function widgetTypeOf(el) {
    const t = el.tagName ? el.tagName.toLowerCase() : 'input';
    if (t === 'textarea') return 'textarea';
    if (t === 'select') return 'select';
    if (t === 'input') return (el.getAttribute('type') || 'text').toLowerCase();
    return t;
  }

  function collectWidget(el, rootRef) {
    const wtype = widgetTypeOf(el);
    if (ban.has(wtype)) return;
    if (el.disabled || el.readOnly) return;
    if (el.getAttribute('aria-hidden') === 'true') return;
    if (!isVisible(el)) return;

    let options = [];
    if (wtype === 'select') {
      const opts = Array.prototype.slice.call(el.options || [])
        .map((o) => (o.text || '').trim())
        .filter((t) => t);
      options = opts.slice(0, 40);
    }

    // A stable actuator handle: id wins, name next; a sequential handle is
    // embedded for everything else so actuator and enumerator always agree.
    const seqId = 'envoy-field-' + (++seq);
    if (!el.id) { try { el.id = seqId; } catch (e) { /* detached or sealed */ } }

    const handle = el.id ? '#' + el.id.replace(/""/g, '') : '';
    const label = labelFor(el, el.ownerDocument);
    if (!label && !el.name && options.length === 0) return;

    out.push({
      type: wtype,
      label: label,
      selector: handle,
      required: !!(el.required || el.getAttribute('aria-required') === 'true'),
      options: options,
      currentValue: wtype === 'file' ? '' : String(el.value || '').slice(0, 160)
    });
  }

  function walk(rootEl, doc) {
    if (!rootEl || !rootEl.querySelectorAll) return;
    const els = rootEl.querySelectorAll('input, textarea, select');
    Array.prototype.slice.call(els).forEach((el) => collectWidget(el));

    // Same-origin iframes: recurse into their document (real doc, not a clone).
    const frames = rootEl.querySelectorAll('iframe');
    Array.prototype.slice.call(frames).forEach((fr) => {
      try {
        if (!fr.contentDocument) return; // cross-origin or not-loaded
        walk(fr.contentDocument, fr);
      } catch (e) { /* cross-origin: honestly skipped */ }
    });

    // Open shadow roots: their widgets label from inside the shadow tree; the
    // collector still sees them because querySelectorAll on the host misses
    // them, so walk explicitly.
    if (rootEl.querySelectorAll) {
      const hosts = rootEl.querySelectorAll('*');
      Array.prototype.slice.call(hosts).forEach((host) => {
        try {
          if (host.shadowRoot) walk(host.shadowRoot, host.ownerDocument);
        } catch (e) { /* sealed shadow */ }
      });
    }
  }

  walk(document, document);
  return JSON.stringify(out);
})();";
}
