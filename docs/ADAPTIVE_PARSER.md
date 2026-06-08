# Adaptive Parser (Self-Healing Element Locator)

The Adaptive Parser makes Envoy's site templates self-healing. When a template's primary CSS selector fails to find an element (because the site changed its DOM), the parser attempts to relocate the element by matching its structural fingerprint against DOM candidates before falling through to Safe Mode.

## How It Works

Each template step can optionally include a `fingerprint` object that captures structural traits of the target element. When the primary selector (and fallback selector) fail to match, the `ElementLocatorService` walks the current DOM via CDP, scores every candidate element against the fingerprint, and rebinds to the best match if it clears a configurable confidence threshold. If no fingerprint is provided, or the match score is below threshold, the existing Safe Mode behavior is preserved unchanged.

### The Resolution Flow

```
Step has selector?
  → Yes: Query DOM with selector
    → Found? → Use it (confidence 1.0)
    → Not found? → Step has fallback_selector?
      → Yes: Query DOM with fallback
        → Found? → Use it (confidence 0.9)
        → Not found? → Step has fingerprint?
          → Yes: Snapshot DOM, score candidates
            → Best score >= threshold? → Relocate to new element
            → Below threshold? → Return null (triggers Safe Mode)
          → No: Return null (triggers Safe Mode)
  → No: Return null
```

## Fingerprint Schema

Add a `fingerprint` object to any step in a template JSON. All sub-fields are optional; a step without a `fingerprint` keeps its current behavior exactly.

```json
{
  "action": "fill",
  "field_id": "email",
  "selector": "#email-input",
  "fingerprint": {
    "tag": "input",
    "attributes": {
      "type": "email",
      "name": "email",
      "placeholder": "Email Address"
    },
    "label_text": "Email Address",
    "text_content": null,
    "ancestor_chain": ["form#application-form", "div.field-row"],
    "siblings_before": ["First Name", "Last Name"],
    "siblings_after": ["Phone Number"],
    "position_index": 2
  }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `tag` | string? | HTML tag name (hard filter, not scored) |
| `attributes` | object? | Key-value pairs of HTML attributes. Important attributes (`name`, `type`, `id`, `placeholder`) are weighted more heavily than less stable ones (`class`) |
| `label_text` | string? | Text from an associated `<label>` or `aria-label` attribute. Matched via normalized Levenshtein similarity |
| `text_content` | string? | Inner text content of the element (currently reserved, not used in scoring) |
| `ancestor_chain` | string[]? | CSS-like selectors from outermost ancestor to direct parent. Scored by longest matching suffix |
| `siblings_before` | string[]? | Text content of sibling elements that appear before this one |
| `siblings_after` | string[]? | Text content of sibling elements that appear after this one |
| `position_index` | int? | 0-based index among same-tag siblings within parent |

## Authoring Fingerprints for New Templates

1. Open the target job site in Chrome with DevTools
2. Identify each form element you need to fill or click
3. For each element, collect:
   - HTML tag (`input`, `select`, `button`, etc.)
   - Key attributes that are likely to persist (`name`, `type`, `id`, `data-*` attributes)
   - The visible label text (from `<label for="...">` or `aria-label`)
   - The parent chain (2-3 levels of ID/class-based selectors)
   - Text of nearby sibling elements (before/after)
   - The element's position among same-tag siblings
4. Add the `fingerprint` block to each template step
5. Imperfect fingerprints degrade gracefully — the primary selector is always tried first

## Scoring Weights

| Component | Weight | Method |
|-----------|--------|--------|
| Attribute overlap | 0.30 | Weighted Jaccard — `name`/`type`/`id` worth more than `class` |
| Label proximity | 0.30 | Normalized Levenshtein similarity |
| Ancestor chain | 0.15 | Longest matching suffix ratio |
| Sibling context | 0.15 | Token overlap of before/after sibling text |
| Position index | 0.10 | 1 - (distance / 10), clamped to [0, 1] |

**Default threshold:** 0.75. A candidate must score >= 0.75 to trigger relocation.

## Threshold Configuration

The confidence threshold is configurable via `EnvoySettings`:

```csharp
services.AddSingleton<EnvoySettings>(new EnvoySettings
{
    RelocationConfidenceThreshold = 0.80 // stricter (default: 0.75)
});
```

Lower values (0.5-0.7) allow more aggressive relocations; higher values (0.8-0.95) require closer matches.

## Relocation Logging

When a relocation occurs (`DidRelocate == true`), Envoy writes a structured JSON entry to:

```
%LOCALAPPDATA%/Envoy/relocations.jsonl
```

Each line is a JSON object:

```json
{
  "templateId": "greenhouse-default",
  "field": "email",
  "originalSelector": "#email",
  "newSelector": "input[type='email']",
  "fingerprint": { "tag": "input", "attributes": { "type": "email" }, "labelText": "Email Address" },
  "score": 0.92,
  "timestamp": "2026-05-04T12:34:56Z"
}
```

Community contributors can use this log to identify selector drift and submit template updates via pull requests.

## Architecture

| Component | File | Responsibility |
|-----------|------|----------------|
| `Fingerprint` model | `TemplateEngine.cs` | Deserialization model for fingerprint JSON |
| `DomCandidate` model | `DomScorer.cs` | In-memory DOM candidate extracted from the browser |
| `DomScorer` | `DomScorer.cs` | Pure scoring functions (no CDP dependency, fully testable) |
| `IElementLocator` / `LocateResult` | `IElementLocator.cs` | Service interface and result record |
| `ElementLocatorService` | `ElementLocatorService.cs` | Orchestrates selector lookup, fallback, fingerprint relocation via CDP |
| `IBrowserQuery` / `CdpBrowserQueryAdapter` | `IBrowserQuery.cs` / `CdpBrowserQueryAdapter.cs` | Abstraction over CDP for testability |
| `RelocationLogger` | `RelocationLogger.cs` | Appends relocation diffs to JSONL |
| Tests | `tests/Envoy.Core.Tests/` | Unit tests for scoring and locator logic |

## Backward Compatibility

- Templates **without** a `fingerprint` field work unchanged
- Primary selector matches **always** short-circuit before any fingerprint logic runs (no performance impact)
- Safe Mode **still** triggers when no element is found (no behavior change)
- No new NuGet dependencies were added