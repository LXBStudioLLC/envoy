# Authoring Site Templates

Site templates are the highest-leverage way to contribute to Envoy. A template teaches Envoy how to fill out a specific job-application form: which fields to fill, in what order, what values to put in each, and whether (or never) to click Submit. This guide walks through adding a new template end-to-end using SmartRecruiters as the example.

If you're adding a template, you do **not** need to know C# — templates are pure JSON.

## TL;DR

1. Copy an existing template that's structurally close to your target site (e.g. `src/Envoy.Templates/greenhouse-default.json`).
2. Rename it to `<board>-<variant>.json` (e.g. `smartrecruiters-default.json`).
3. Update `id`, `name`, `url_match`, and rewrite the `steps` array to match the form you see on the target site.
4. Add a `fingerprint` block on every fill/click step so the adaptive parser can recover when the site's DOM changes.
5. Run the app, navigate to a real job posting, and watch Envoy fill the form. Iterate.
6. Open a PR with a screenshot + the URL you tested against.

## File location and naming

All templates live in `src/Envoy.Templates/` as standalone JSON files. They get loaded automatically at startup — no code changes required. The filename convention is:

```
<board>-<variant>.json
```

Examples already in the repo: `linkedin-easy-apply.json`, `greenhouse-default.json`, `workday-default.json`, `lever-default.json`, `indeed-apply.json`.

For a new board, name it `<board>-default.json` unless there's a second variant for the same site.

## Template schema

A template is an object with three top-level metadata fields and an array of `steps`:

```json
{
  "id": "smartrecruiters-default",
  "name": "SmartRecruiters Application",
  "url_match": "jobs.smartrecruiters.com/*",
  "version": "1.0.0",
  "steps": [
    /* ... see Step types below ... */
  ]
}
```

| Field | Type | Required | Purpose |
|-------|------|----------|---------|
| `id` | string | yes | Unique identifier; convention is the filename without `.json`. |
| `name` | string | yes | Human-readable label shown in the UI / logs. |
| `url_match` | string | yes | Glob pattern. The template applies when the active tab URL matches. |
| `version` | string | yes | Semantic version. Bump when you change step structure. |
| `steps` | array | yes | Ordered list of actions. Envoy runs them top-to-bottom. |

## Step types

Each step has an `action` field. Supported actions:

### `wait_for`
Block until a selector resolves. Use at the start to confirm the form has rendered before filling.
```json
{ "action": "wait_for", "selector": "form[data-test=application-form]", "timeout": 10000 }
```

### `fill`
Type into a text field. The value comes from the master profile based on `field_id`.
```json
{
  "action": "fill",
  "field_id": "first_name",
  "selector": "#firstName",
  "fingerprint": { /* see below */ }
}
```

Recognized `field_id` values: `first_name`, `last_name`, `email`, `phone`, `address`, `city`, `state`, `zip`, `linkedin_url`, `portfolio_url`, `current_company`, `current_title`, `years_experience`, `cover_letter`. Custom IDs fall back to "skip" unless you handle them with `value_from`.

### `upload`
Attach a file. For the tailored resume PDF, use `"value_from": "generated_pdf_path"`.
```json
{
  "action": "upload",
  "field_id": "resume",
  "selector": "input[type='file'][name='resume']",
  "value_from": "generated_pdf_path",
  "fingerprint": { /* ... */ }
}
```

### `select`
Pick an option from a dropdown.
```json
{
  "action": "select",
  "field_id": "work_authorization",
  "selector": "select[name='workAuth']",
  "value": "Yes, I am authorized to work in this country"
}
```

### `click`
Click any element (e.g. "Continue" between multi-step forms).
```json
{ "action": "click", "selector": "button[data-test=continue-step-1]" }
```

### `conditional_click`
Click only if user confirms first. **Always use this for the Submit button.** Setting `require_confirmation: true` triggers Envoy's Safe Mode review prompt before the click happens.
```json
{
  "action": "conditional_click",
  "description": "Submit SmartRecruiters application",
  "selector": "button[type='submit'][data-test=submit-application]",
  "require_confirmation": true,
  "fingerprint": { /* ... */ }
}
```

## Fingerprints — make the template self-healing

Every fill / upload / select / conditional_click step should include a `fingerprint`. Without one, the adaptive parser cannot recover when the site changes selectors, and your template will fall through to Safe Mode the day SmartRecruiters renames a class.

```json
"fingerprint": {
  "tag": "input",
  "attributes": {
    "type": "text",
    "name": "firstName",
    "id": "firstName"
  },
  "label_text": "First name",
  "ancestor_chain": ["form[data-test=application-form]", "div.field-group"],
  "siblings_before": [],
  "position_index": 0
}
```

All sub-fields are optional, but more signals = better resilience. The scoring weights and full schema are documented in [ADAPTIVE_PARSER.md](ADAPTIVE_PARSER.md). The minimum useful set is: `tag` + `label_text` + 2-3 stable `attributes`.

## How to capture fingerprints from a real site

1. Open the target job posting in Chrome.
2. Open DevTools (F12), inspect each form field.
3. Note the tag, the stable attributes (avoid auto-generated IDs like `id="input-7f2a9b3c"` — those rotate per page load), the label text rendered next to the field, and the chain of meaningful ancestors up to the `<form>`.
4. Write all of that into the `fingerprint` block.

## Testing your template

1. Build the project: `dotnet build -c Release`.
2. Run the app: `dotnet run --project src/Envoy.UI`.
3. In the Dashboard, drop a resume PDF and paste a real job URL that matches your `url_match`.
4. Open Chrome's remote-debugging session so Envoy can attach, and watch the form fill in real time.
5. Inspect `%LOCALAPPDATA%\Envoy\relocations.jsonl` if any step relocated via fingerprint. If a step fell through to Safe Mode, that's where the trace lives.
6. Iterate on selectors and fingerprints until the run completes without falling back. The Submit click should still require confirmation — that's by design.

## Multi-step forms

Many boards split applications across pages or accordions. Pattern:

```json
"steps": [
  { "action": "wait_for", "selector": "...page1 marker..." },
  /* page 1 fills */
  { "action": "click", "selector": "button[data-test=next]" },
  { "action": "wait_for", "selector": "...page2 marker..." },
  /* page 2 fills */
  { "action": "conditional_click", "selector": "button[type=submit]", "require_confirmation": true, "fingerprint": { ... } }
]
```

## Do not

- Hard-code personal data in templates. All values come from the user's master profile or the tailored resume PDF.
- Click Submit without `require_confirmation: true`. Envoy's safety story depends on never submitting without explicit user OK.
- Use auto-generated CSS selectors (`#mui-7`, `.css-abc123`) without a backing fingerprint — they will break.
- Include the user-supplied job URL or any personal info in commit messages or screenshots when you open a PR.

## Submitting your template

1. Open a PR with branch `template/<board-name>`.
2. Include a representative job URL you tested against in the PR description.
3. Attach a screenshot of the filled form (before clicking Submit).
4. Add a bullet under the `[Unreleased]` section in `CHANGELOG.md`: `- <Board name> template (#PR)`.
5. CI must be green.

Maintainers will smoke-test the template against a different live posting on the same board before merging.
