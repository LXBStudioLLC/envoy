# The Applications Escalator — apply-throughput plan

> Adopted 2026-11-05. This plan refocuses Envoy from "ghost-job detector with an
> apply copilot" to **"one profile, one queue, maximum applications filled out
> with the least hassle."** Ghost detection stays: it is the quality rail that
> makes volume *safe* (don't waste an application on a posting that will never
> hire). every work item below is measured against two numbers:
>
> 1. **verified applications per hour** — submissions that actually reached a
>    live company ATS (not a stale aggregator copy),
> 2. **taps per application** — user interactions from "I want this job" to
>    "submitted".
>
> Work items are `APP-01` … `APP-17`. P0 = make the funnel exist, P1 = the
> throughput engine, P2 = volume, safely, P3 = "least hassle" polish.

## Evidence base (what audits + research found, Nov 2026)

- **Architecture verdict:** direct-to-company-ATS submission with verified
  delivery is the top architectural tier for auto-apply; competitors that
  route through aggregators dominate the category's failure modes (stale
  listings, unverified delivery, BBB "F" ratings, ToS exposure). Envoy's
  public-ATS-API discovery → fill-on-the-company's-own-page spine is already
  in the correct tier. The constraint set (no login scraping, no CAPTCHA
  solving, human-gated submit, aggregators copilot-only) is a *feature* — it
  is the delivery-verification and account-safety story that the failing
  competitors lack.
- **Quality-vs-volume:** recruiters track velocity patterns; the market
  backlash to "AI slop" applications (hundreds per posting) makes raw blast
  counterproductive. Max *converting* applications beats max raw: per-app
  tailoring, duplicate suppression, ghost-risk filtering, delivery evidence.
- **Code truth (audits, 2026-11-05):** the engine is a single-job,
  single-page, human-gated copilot: 5 glob templates (2 on aggregator sites),
  `select` action inert, 10 documented profile field IDs silently fill `""`,
  no checkboxes/radios/iframes/shadow DOM, no multi-page wizard support, no
  post-submit verification, one CDP session (no parallel tabs), no queue, no
  duplicate prevention, unbounded local-LLM latency, dev builds ship **no
  templates** (publish-only), and zero tests on the orchestrator/browser
  path. Find Jobs → Apply is a hand-copy-URL island hop that discards the
  ghost snapshot.
- **Benchmarks:** best agent pipelines ~17 apps/hour; manual applies 2.5–44
  min depending on autofill and form depth. Realistic Envoy targets: 10–30
  verified apps/hour on Greenhouse/Lever/Ashby/Workable/Recruitee with the
  queue; Copilot-speed for aggregators.
- **ATS landscape (template ROI):** iCIMS ~10.7% share #1, Oracle/Taleo,
  Workday, Greenhouse, SmartRecruiters. Enterprise ones are login-walled and
  are where generic LLM field-mapping beats per-site templates.

## The ladder (products)

1. **Autofill Copilot (ship well, it is the product)** — one click, generic
   form mapping, review once, submit gated. Works on any ATS without a
   shipped template.
2. **Gated queue (near-term batch)** — select N jobs → "Apply to all" →
   queue prepares each → rapid review stream (1-tap confirm each). |
   Aggregators stay gated-by-classifier here too.
3. **Full-Auto (company/ATS sites only)** — per-site trust (after the user
   has approved the same site 3× or flips an explicit opt-in), unattended
   overnight with a morning report; CAPTCHA ⇒ hand off + log.

## Work items

### P0 — make the funnel exist

- `APP-01` **Templates ship in dev builds.** `Envoy.Templates.csproj` copies
  `*.json` to output; `dotnet run` is a working apply engine. (Doc claim vs
  build reality gap; publish.ps1 is not the product.)
- `APP-02` **Find Jobs → Apply, connected.** APPLY button on every result
  row; carry `JobPosting` + `GhostScoreSnapshot` into the apply session (no
  re-scrape, no re-score); SKIP/VIEW retained.
- `APP-03` **Session continuity + cancellability.** No CDP teardown between
  tailor and fill; one connection serves both; bounded LLM completion
  timeouts; cancel button; hung-Ollama can no longer freeze the flow.
- `APP-04` **Duplicate prevention.** Submit consults `JobEvents` +
  `ApplicationLog` via `PostingKey`: already-applied ⇒ a clear
  "you applied N days ago" state instead of a silent re-submit.
- `APP-05` **Honest completion.** After a fill: count filled required
  fields; after submit: verify success signal (post-submit URL/state);
  ApplicationLog records filled/required and verification; no more
  `Completed` when nothing happened.
- `APP-06` **Vault edits work history/education/projects.** The data that
  feeds every resume must be fixable in-app.

### P1 — the throughput engine

- `APP-07` **Application queue core.** SQLite-backed queue (PostingKey
  dedupe; statuses Queued/Tailoring/AppReady/NeedsReview/NeedsCaptcha/
  Submitted/Failed); queue worker owns the CDP session; dead
  `Pending/Blocked` statuses replaced.
- `APP-08` **Parallel signal scoring.** 9 ghost signals currently sequential.
- `APP-09` **Generic LLM field mapper (coverage unlock).** Snapshot visible
  form fields (locator JS extended: iframes + shadow DOM), LLM maps labels →
  profile/`Vault Q&A` answers; deterministic fill through existing DOM
  actions; per-ATS-family map cache. Target: any mainstream ATS reachable
  without a hand template.
- `APP-10` **Profile field expansion + Vault Q&A bank.** Address/city/state/
  zip, portfolio, GitHub, current company/title/years, work authorization,
  salary expectations, relocation, EEO self-ID, references; user answers
  knockouts once, Envoy reuses (review-before-submit always).

### P2 — volume, safely

- `APP-11` **Batch apply + in-code classifier.** Multi-select → queue;
  rapid-review stream; per-company throttle + daily cap. Build the
  **in-code site classifier** (aggregators list) that AGENTS.md already
  promises, before any Full-Auto exists.
- `APP-12` **Full-Auto mode.** Explicit per-site/global opt-in; overnight
  drain; morning report (verified/fail/CAPTCHA-handoff).
- `APP-13` **Multi-tab CDP.** Per-target sessions (no shared `_sessionId`);
  2–3 parallel queue workers.
- `APP-14` **Deliverability evidence.** `ApplicationLog` stores post-submit
  verification artifacts; the scoreboard reports verified completion rates
  (the market's differentiator).
- `APP-15` **Retries and resumption.** Re-fill from a saved TailoredProfile
  without a new LLM run; partial-fill checkpointing; captcha hand-off keeps
  the filled form in the browser.

### P3 — least-hassle polish

- `APP-16` **Onboarding + notifications.** First-run wizard (profile → LLM →
  browser → first application), tray + progress notifications, "Night Owl"
  drain scheduler; dead settings knobs wired or removed honestly.
- `APP-17` **Scoreboard becomes throughput-first.** Verified applications
  today/this week, taps-per-application trend, queue status header.

## Hard constraints (unchanged, from AGENTS.md)

1. Automation is first-class and improves, never shrinks.
2. Full-Auto is scoped to company/ATS career sites; the in-code classifier
   enforces aggregator copilot-only. (**APP-11** builds the classifier.)
3. On a CAPTCHA/bot-check: surface + log + hand off. Never evade.
4. Data layers read public data; no auth bypass. Applying is the *user's own
   action in their own browser session*; Envoy automates it.
5. Ghost detection stays risk+confidence+evidence, never verdicts.
6. .NET 8, file-scoped namespaces, feature-branch → PR → CI, no network in
   tests (except explicitly real-network harness runs outside test projects).

## Sequencing

APP-01 → 02 → 03 → 04 → 05 → 06 (P0 first, each its own PR);
then 07 → 08 → 09 → 10; then 11 → 12 → 13 → 14 → 15; P3 continuous.
