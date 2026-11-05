# Changelog

All notable changes to Envoy are documented in this file. Format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); the project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.2.0] - 2026-11-05

### Added — #92 remainder shipped: the shell of the product

- **Dashboard first-run checklist, fully wired.** The readiness strip now ends where the product starts: profile → LLM configured → browser bridge → templates present → debug port → **first application** (finished attempts in the ledger count; parked captcha rows don't). Every incomplete row carries one action — IMPORT PDF / SET UP AI / LAUNCH BROWSER / SHOW FOLDER / SHOW SETTINGS / FIND JOBS — and the strip auto-hides the moment everything is green.
- **Tray presence.** A notification-area icon lives while Envoy runs: live queue counts ("queue: 2 waiting · 1 ready · 4 sent today"), click to restore, right-click menu with EXIT. Closing the window minimizes to the tray so a draining queue stays alive; menu → EXIT is the real way out.
- **Notifications on what needs you.** Balloon toasts for: ready to fill, captcha hand-off ("Envoy never solves them"), needs-your-send, queue failures, queue finished (sent/failed counts), and WARN/PERM public-data refresh completion — coalesced so a drain never becomes a wall of popups.
- **Night Owl drain window.** The Full-Auto page gains START/END UTC hour fields and an ANY TIME clear (settings example documented). The worker now wakes on the window: rows prepared outside the drain window (or before Full-Auto was enabled) are re-consulted and submitted the moment the window opens — no manual nudge, never a re-prepare.
- **Taps per application, measured honestly.** The journey click stream (queued → opened → filled → gate confirm) now records one ledger row per committed action; the Scoreboard shows average taps per application that finished in the last 30 days, joined by posting identity — the formula is stated on screen, and batch queueing stays visible as amortization, not hidden.
- **Full-Auto trust-list fix found during the Night-Owl work.** `HostOf` demanded an absolute URL, so a bare-host trust entry ("boards.greenhouse.io", the documented form) could never match — explicit trust lists were silently dead. Scheme-less entries parse now; regression test pins it.

### Added — multi-profile queue (multi-resume support)

- **Queue rows carry their resume.** Every queued posting is stamped with the profile it was queued for; the background worker tailors for whichever row is oldest regardless of which resume it belongs to — a second profile's rows no longer starve behind the first profile's batch. Legacy rows persisted before stamping keep the old single-profile binding, so upgrades stay smooth.
- **Find Jobs `APPLY WITH` selector.** With several resumes saved, QUEUE / QUEUE SELECTED and per-row APPLY use the picked profile instead of demanding a Dashboard detour; the choice rides along into the Apply view. Saved rows re-check their profile on every visit (a deleted profile falls back to the first).
- Sort options in Find Jobs now order on carried data — the real posting date and the numeric risk score — instead of display strings that sorted by board name and put unscored "OK" on top of every risk.

### Fixed — the deep audit (three batches, #97 / #98 / #99)

- Full-Auto drain maps the generic-fill outcome honestly: a fill that hands the Send to you parks the row as NEEDS YOUR SEND — it never inflates the submitted count.
- The generic-fill path keeps the browser page open for your Submit instead of closing it out from under the completed form.
- CDP page-session command-response race removed; JS selectors and values are JSON-encoded before injection (multiline answers no longer produce malformed script); selects match only on exact option text.
- The Apply view guards cross-flow entry: a running tailoring/fill can no longer be stomped by a second discovery tap or button press, and the needs-your-send outcome reports in plain language.
- Queue items carry their master profile from the UI click (background tailoring could never match a profile before).
- Batch-queue checkboxes are wired; QUEUE SELECTED no longer clobbers the results list.
- The configured debug port is honored everywhere (probes, auto-launch, submit connection), not just port 9222.
- A browser restart kills only processes running Envoy's own profile, never the user's personal browser windows.
- Blocked duplicates are transient reasons, not accumulated queue rows; the queue panel subscribes once, not per navigation.
- The outcome combo reverts when the database refuses the save; the PDF generator is registered singleton.

### Added — the Applications Escalator (apply-throughput)

The apply flow is now a queue, not a form wizard. One profile, one queue, maximum honest applications; ghost detection stays the quality rail. Docs: [docs/APPLY-THROUGHPUT.md](docs/APPLY-THROUGHPUT.md).

- **One-click apply from Find Jobs.** Every result row carries an APPLY button; the posting and its ghost score travel with the click, tailoring runs without re-scraping, and the result panel is ready immediately. A QUEUE button adds the posting to the background queue instead.
- **The application queue.** A SQLite-backed queue tailors every queued posting in the background with no browser use at all, then waits for your one-click fill. A live YOUR QUEUE panel shows counts by status (QUEUED → TAILORING… → READY TO FILL → …), with APPLY NOW on each ready item. One active application per company; a daily cap protects against velocity flags (default 25/day, `QueueDailyCap`).
- **Duplicate prevention.** Submitting to a posting already completed is blocked with its prior date (canonicalized by URL, tolerant of mirror boards and tracking parameters). Re-apply is possible, but only after an explicit prompt that defaults to no.
- **Honest completion.** A template run counts what it actually filled; required fields that came back unfilled fail the run with the real numbers. After a submission, a verify block records whether the delivery could be confirmed. Per-application evidence: required/filled counts, verified flag, verification note.
- **The Scoreboard tracks verified delivery.** Verified sends vs sends the delivery check couldn't confirm — stated separately, never folded. A per-day verified distribution feeds the weekly view. Failed captchas and duplicate blocks are not submissions.
- **Retries without waste.** Preparing the same posting twice reuses the tailored profile: no LLM re-run, no duplicate rows.
- **Full-Auto mode (off by default).** A guarded toggle. The queue can submit prepared applications without asking per site — only on the company's own ATS boards, and only for trusted hosts: three verified submissions, or an explicit list. Aggregators are structurally excluded (a SiteClassifier decides in code; unknown domains fail closed). An optional UTC hour window gates the drain. CAPTCHAs hand off to you, never solved.
- **Generic form auto-fill (LLM field mapper).** When no template matches, Envoy enumerates the visible widgets (main document, same-origin iframes, open shadow roots) and maps them with strict rules: values only from a whitelisted profile field or a stored answer; selects must match an option exactly; anything unmapped is NEEDS-HUMAN. Inventions are rejected structurally. Stored answers live in a local Q&A bank managed in the Vault.
- **Cover letters.** Tailoring also writes a 150-220 word cover letter from the resume's facts only (a timeout never breaks the run), stored with the tailored profile and as plain text beside the resume PDF.
- **Documented template field IDs now all resolve:** linkedin_url, portfolio_url, current_company, current_title, years_experience, city/state, cover_letter — previously ten documented IDs silently filled nothing.
- **Cancellable runs + session continuity.** TAILOR and FILL gain a working CANCEL; the LLM completion is bounded (default 180 s); the page opened while preparing is reused at submit.
- **Readiness strip on the Dashboard.** A per-step READY TO RUN checklist with a plain-language fix for the one step that is not ready.
- **Per-page browser sessions.** Independent per-tab CDP sessions (command routing, events, captcha sniff per page) lay the ground for parallel workers.

### Fixed
- Templates ship with every build: a plain dotnet run previously failed every application with "No template found for this job board".
- Settings knobs that loaded but did nothing are now wired (or honest): TypingSpeedVariance/MousePathSteps feed the human-cadence emulator, ChromeDebuggingPort is used by the bridge, TemplatesPath relocates the template folder, DefaultMode pre-selects the run mode.
- The Lever template's LinkedIn field resolved to nothing (wrong field id); it now fills.
- Ghost-detection suites no longer race on shared dataset hooks; ghost signals evaluate in parallel (nine checks cost the slowest, not the sum).

### Changed
- README/AGENTS/CONTRIBUTING synced: nine shipped signals, all starter signal issues closed, roadmap updated (Full-Auto implemented behind the guarded toggle). `docs/APPLY-THROUGHPUT.md` records the architecture and the constraint boundary (aggregators copilot-only in code, CAPTCHAs hand off, human gates everywhere Full-Auto doesn't reach).

## [1.1.0] - 2026-07-18

### Changed
- The whole app now talks like a person. Every emoji and dingbat is gone from buttons and status messages, along with the em dashes and the mission-briefing jargon. Mission Briefing is now just Apply, the Data Vault is the Vault, Browser Nexus is Browser, LLM Nexus is AI Models, and Codename is Full Name. Status lines read like plain English ("Application submitted." instead of "MISSION ACCOMPLISHED"). The Browser page also drops the fingerprint-evasion sales pitch and now just explains how the DevTools connection works, which matches what the app actually does and has always done.

### Added
- Response tracking. The scoreboard lists your submitted applications with a dropdown to log what came back: got a reply, interview, offer, or rejected. Once you've sent five, the board shows your application-to-response rate. Rejections count as responses, since the stat measures whether anyone answered at all. Every logged outcome also lands in the local ledger with the posting's ghost score, so future versions can compare outcomes against risk.
- The Scoreboard. A new view, and the first thing you see when Envoy opens: ghosts dodged, hours saved, day streak, and ghosts surfaced, with a receipts list showing every dodge alongside the evidence that was on screen when you passed. Hours saved shows its math (dodges times your own time-per-application estimate, adjustable right on the board) instead of pretending to measure something it can't. Screened and sent counts appear only as context; the board scores the system, it does not grade you.
- Find Jobs feeds the ledger. Every scored result is recorded as a sighting (one per posting per day, so re-running a search inflates nothing), opening a posting records a view, and a new SKIP button on each result records the pass and clears the row. Skipping a flagged posting answers with GHOST DODGED in the status line. Day-by-day sightings are also the listing history the repost-frequency signal needs for its stronger path.
- Scoreboard foundation. Envoy now keeps a local activity ledger (a `JobEvents` table in `envoy.db`): a submitted application is recorded as Applied, and a cancel at the submit gate as Declined, each carrying the ghost risk score, band, and evidence that was on screen when the user decided. Application logs also store the score and band at submit time. Cancelling at the gate gets its own status (`DeclinedByUser`) instead of being lumped in with the safety-check halt, and the Apply view shows it in yellow, not error red, because walking away from a bad posting is a decision, not a failure. This ledger is the data layer for the upcoming stats view, and it begins the cross-session listing history the repost-frequency signal has been waiting on.
- In-app update check. Once per launch, Envoy asks the public GitHub releases API whether a newer version exists and, when one does, shows an UPDATE link in the title bar that opens the release page. Nothing is sent beyond the request itself; set `"CheckForUpdates": false` in `settings.json` to turn it off.

## [1.0.3] - 2026-07-14

### Added
- Find Jobs overhaul: 80+ seed companies across six ATS platforms, a new SmartRecruiters source with pagination, auto-discovery that probes all six platforms when you type a company name, board add/remove in the UI (persisted to `seed-boards.json`), a five-minute response cache, result sorting (newest, ghost score, company), and ATS-aware Brave web search.

### Fixed
- Location filter no longer floods every search with unrelated remote jobs.
- Critical audit fixes: the browser bridge awaits its previous receive loop before starting a new one; repositories and the orchestrator now use singleton lifetimes over the context factory; cancel-path cleanup uses `CancellationToken.None` so it still runs; a safe cast keeps a crash from escaping an async void handler; all `Run.Text` bindings are one-way.

## [1.0.2] - 2026-07-05

### Fixed
- Ollama settings crash. Model-list bindings to read-only properties threw when the LLM settings view rendered (`Run.Text` binds two-way by default); all such bindings are now `OneWay`.
- A full code-review pass fixed 53 verified bugs. Highlights: resume PDFs now include the contact header (name, email, phone) that was being omitted; the LinkedIn template matches URLs again; Google Gemini returns its model list; the safety gate no longer crashes on unusual LLM output; settings and DPAPI key-save failures surface in the UI instead of failing silently; the browser bridge disposes cleanly and serializes its sends; and a captive `DbContext` (unbounded growth, concurrent-access crash) was replaced with a per-operation context factory. Also: cancellation is honored in job discovery, Greenhouse posting dates feed the age and repost signals correctly, keyword-density matching no longer counts "java" inside "javascript", plus a batch of null guards, culture-invariant casing (Turkish-locale safe), and resource-leak fixes.

### Added
- The application, its window, the installer, and the shortcuts now carry the Envoy ghost icon.

## [1.0.1] - 2026-07-05

### Fixed
- **Ghost signals: honest activity.** `DuplicateJdSignal` was inert at runtime: its comparison corpus (`Extra["dupcheck.corpus"]`) was never populated outside tests, so it always returned null. It is now fed a **same-batch corpus** built from the other postings in each discovery run (sanctioned data already in hand, no extra requests), so it fires on cross-company near-duplicate descriptions. Docs corrected to state that **four** signals are active in the running app (ATS Cross-Check, Posting Age, Duplicate JD, Scam Pattern). **Repost Frequency** remains implemented and unit-tested but dormant until cross-session listing history is persisted, which this build does not ship. Known limitation: Posting Age is active in the Find Jobs list but not yet in the Apply panel (which does not capture a posting date).
- **Data-source audit.** Verified every discovery endpoint (Greenhouse, Lever, Ashby, Workable, Recruitee, Brave) is a current, public, sanctioned API used without scraping or anti-bot evasion.
- `install.ps1` no longer risks deleting the user's `%TEMP%` during cleanup, and targets the correct `Envoy.exe`.

### Added
- Rolling file logging to `%LOCALAPPDATA%\Envoy\logs` and user-visible crash dialogs (failures were silent before).
- About / report-a-bug affordance in the title bar, showing the real app version.
- Full canonical AGPL-3.0 `LICENSE` text and a `THIRD-PARTY-NOTICES.md` (NuGet licenses + bundled-font OFL texts), shipped alongside the binary.

### Changed
- Single-sourced the version via `Directory.Build.props`; the built `Envoy.exe` now carries the real version (previously always stamped 1.0.0.0).

## [1.0.0] - 2026-06-23

Go-live release. Ghost-job detection is now a first-class, in-app feature, and Envoy can discover jobs from sanctioned public sources.

### Added
- **Ghost detection wired into the app.** `AddEnvoyGhostDetection()` runs at startup; every prepared application shows a **Ghost Risk** panel (risk band, confidence, and human-readable evidence) in the Apply view.
- **Find Jobs view + `Envoy.Discovery` module.** Sanctioned job discovery that reads **public, unauthenticated ATS board APIs** (Greenhouse, Lever, Ashby, Workable, Recruitee) and an **official, key-gated web-search API** (Brave Search, where you supply your own key, stored DPAPI-encrypted). No scraping behind authentication, no anti-bot evasion, no CAPTCHA bypass. Every discovered posting is ghost-scored and shown with a risk badge.
- **Scam Pattern signal** (Deterministic, local regex): flags off-platform interview redirects (Telegram/WhatsApp), upfront fee / PII asks, crypto / gift-card payment demands, and check / overpayment fraud. Precision-first; evidence describes the pattern, never a verdict on a named company.
- `IGhostSignal.RequiresNetwork` so callers can request fast, local-only scoring when ranking many postings at once (e.g. the Find Jobs list).

### Changed
- **Human-gated submit is now truly blocking.** The final submit click waits for an explicit Confirm / Cancel decision in every execution mode; the default Operation Mode is now **Safe**.
- **Stealth input emulation is now a guarded, off-by-default opt-in.** Human-cadence typing / mouse movement only runs after you explicitly enable it in the Browser view behind an acknowledgement (`StealthModeEnabled`); otherwise the form is filled with plain input and only Safe mode is offered. It never bypasses CAPTCHAs and is never used for discovery / scraping.
- Removed the inert `HiringFreezeSignal` and `PermFilingSignal` stubs from the shipped build; they remain tracked as future signals (issues #5 and #1). Envoy ships **five signals**; ATS Cross-Check, Posting Age, and Scam Pattern are active at runtime. (Duplicate JD and Repost Frequency shipped but were not yet fed runtime data; corrected under Unreleased.)

## [0.2.0-beta] - 2026-06-08

Ghost-job detection preview.

### Added
- `Envoy.GhostDetection` signal framework: `IGhostSignal`, `SignalResult`, `GhostScore`, and `GhostScorer` with Neutral / Elevated / High banding (bias for precision over recall).
- Signals: ATS Cross-Check (Deterministic, public Greenhouse/Lever APIs), Posting Age (Probabilistic), Duplicate JD (Weak), Repost Frequency (Weak).
- Fixture-backed xUnit tests with no network calls; reflection-based signal auto-registration.

## [0.1.0] - 2026-05-12

Initial foundation: local-first resume tailoring + human-assisted apply.

### Added
- WPF desktop UI (.NET 8 + HandyControl) with cyberpunk theme, glitch title, custom chrome.
- Local-first LLM pipeline via [OllamaSharp](https://github.com/awaescher/OllamaSharp). Default model: `qwen2.5-coder:14b`.
- Optional cloud LLM providers: OpenAI, Anthropic, Google Gemini. API keys encrypted at rest with Windows DPAPI (`DataProtectionScope.CurrentUser`).
- LM Studio provider for local OpenAI-compatible endpoints.
- Resume PDF parsing via PdfPig + LLM post-processor.
- AI-powered resume tailoring against per-job descriptions with factual integrity diff, keyword density cap, one-page constraint.
- Real-Chrome stealth automation via raw WebSocket → Chrome DevTools Protocol. No bundled browser.
- Human-behavior emulation: Bezier mouse paths, randomized typing cadence, natural scroll.
- Multi-layer safety guardrails (parsing validation, tailoring guardrails, pre-submission verification, runtime anomaly detection). Falls back to Safe Mode when triggered.
- Adaptive parser with structural-fingerprint element locator and JSONL relocation logging at `%LOCALAPPDATA%\Envoy\relocations.jsonl`.
- Site templates: LinkedIn (Easy Apply), Greenhouse, Workday, Lever, Indeed.
- Vault UI for profile history, corrections, and master profile editing.
- LLM Settings view: connection check, model discovery, provider switching.
- Windows installer (Inno Setup + PowerShell variant).
- Crash log capture at `%LOCALAPPDATA%\Envoy\crash.log` covering AppDomain unhandled exceptions, unobserved Task exceptions, and Dispatcher exceptions.
- SQLite-backed local database via EF Core 8 with code-first migrations.

### Documentation
- README, CONTRIBUTING, SECURITY, CODE_OF_CONDUCT, TEMPLATE_AUTHORING, ADAPTIVE_PARSER, SETUP guides.

### Security
- Cloud LLM API keys encrypted with DPAPI at rest. Plaintext keys from pre-DPAPI builds are automatically migrated on first launch.

[Unreleased]: https://github.com/LXBStudioLLC/envoy/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/LXBStudioLLC/envoy/compare/v1.0.3...v1.1.0
[1.0.3]: https://github.com/LXBStudioLLC/envoy/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/LXBStudioLLC/envoy/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/LXBStudioLLC/envoy/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/LXBStudioLLC/envoy/releases/tag/v1.0.0
[0.2.0-beta]: https://github.com/LXBStudioLLC/envoy/releases/tag/v0.2.0-beta
