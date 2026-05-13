---
name: Job-board template request
about: Request support for a new job board, or report breakage on an existing one
title: "[template] "
labels: template
---

## Job board

- Name (e.g. SmartRecruiters, Ashby, JazzHR):
- URL pattern (e.g. `jobs.<board>.com/*`):
- Representative posting URL you tested against (redact identifiers if needed):

## Is this a new template or a fix to an existing one?

- [ ] New template
- [ ] Fix to existing template — which one (filename):

## What goes wrong today

If an existing template is broken, describe the symptom. Paste the relevant relocations.jsonl entries if Envoy fell back to Safe Mode.

```
<paste here>
```

## Form structure

Brief description of the form: single page or multi-step? File upload required? Custom dropdowns? CAPTCHA? Anything unusual.

## Screenshots

Screenshot of the form as it renders today (redact any personal info from the page).

## Have you tried authoring it yourself?

See [docs/TEMPLATE_AUTHORING.md](../../docs/TEMPLATE_AUTHORING.md). PRs are very welcome — even partial templates with fingerprints for the first few fields help a lot.

- [ ] I plan to submit a PR
- [ ] I'd like a maintainer or another contributor to author it
