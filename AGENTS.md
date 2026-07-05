# Envoy — Agent Instructions

## Product Framing

Envoy is a **.NET 8 WPF Windows desktop app** with two pillars:

1. **Ghost-job detection** — scoring how likely a job posting is a waste of an applicant's time, with **transparent evidence** (risk score + confidence + human-readable reasons).
2. **Application automation** — a resume-tailoring + form-fill engine that ranges from a **human-gated copilot** to **full-auto submission**. This is a first-class feature and is meant to be **improved and extended**, not held back.

## Constraints

1. **Automation is first-class; make it better, not smaller.** The apply/form-fill engine spans **Copilot** (user confirms each submit) through **Full-Auto** (unattended submission). Human-cadence input emulation (Bezier mouse paths, typing jitter — the "Stealth" input option) is a **normal, configurable option** — no forced default, no acknowledgement gate. It automates **the user's own actions with the user's own data**.
2. **Full-Auto is scoped to company-owned and ATS-hosted career sites.** Aggregators (LinkedIn, Indeed, Glassdoor, ZipRecruiter, Monster, Dice, etc.) are **Copilot-only by product choice** — a site classifier enforces this **in code**, not just in docs. (Rationale: a block from a company that wasn't going to hire you costs nothing; aggregators are where a block actually hurts the user, so we don't full-auto them.)
3. **On a hard block, hand off — don't fight it.** When a site presents a CAPTCHA, bot-check, or rate-limit, Envoy surfaces it to the user and logs it; it does **not** attempt to defeat the control. **No CAPTCHA-solving, and no techniques whose purpose is to defeat a site's bot-detection.** Full-Auto fails gracefully and yields to the human rather than escalating.
4. **Data layers read public data and don't bypass authentication.** Ghost detection and job discovery use public ATS JSON APIs, key-gated search APIs (e.g. Brave Search), public government datasets, and the posting already in front of the user. **No scraping behind a login.**
5. Ghost detection outputs **RISK SCORE + CONFIDENCE + EVIDENCE** — an assessment with reasons, defaulting to neutral, **not** a bare "FAKE"/"GHOST" label on a named company. (Retained as the product's differentiator and its defamation cover; change deliberately, not by accident. Precision-vs-recall is a tunable default, not a hard rule.)
6. **.NET 8**, nullable enabled, file-scoped namespaces, match existing patterns. Do **NOT** blanket-add `ConfigureAwait(false)` — `Envoy.Core` is consumed in-process by the WPF UI.
7. Work on a `feature/<name>` branch created off `main`. Do **NOT** push directly to `main`. Do **NOT** force-push.

## Build / Test / Run

```powershell
# Restore, build, test
dotnet restore
dotnet build -c Release
dotnet test

# Run the desktop app
dotnet run --project src/Envoy.UI
```

The first launch creates `%LOCALAPPDATA%\Envoy\` for settings, SQLite database, and crash logs.

## Module Map

```
src/
  Envoy.Core/              core services, data layer, LLM providers, browser/CDP
    Services/
      ApplicationOrchestrator.cs   — human-gated single-application flow
      ServiceRegistration.cs       — DI registration (AddEnvoyCore)
      SafetyService.cs             — resume-tailoring guardrails
      DomScorer.cs                 — similarity scoring (reusable for signals)
      LLMDetectionService.cs       — provider discovery/factory
      OllamaService.cs             — local LLM inference wrapper
    Models/
      ApplicationLog.cs
      MasterProfile.cs
      TailoredProfile.cs
  Envoy.GhostDetection/      NEW — ghost-job detection framework
    Models/
      JobPosting.cs
      SignalResult.cs
      GhostScore.cs
    Signals/                         — 5 implemented + tested signals
      AtsCrossCheckSignal.cs       — Deterministic, network (public Greenhouse/Lever APIs)
      ScamPatternSignal.cs         — Deterministic, local regex (scam-pattern detection)
      PostingAgeSignal.cs          — Probabilistic, local (age vs. seniority baseline)
      DuplicateJdSignal.cs         — Weak, local (cross-company near-duplicate JD)
      RepostFrequencySignal.cs     — Weak, local (unchanged re-listing)
      (Hiring Freeze, PERM cross-check — planned; see issues #5 / #1)
    IGhostSignal.cs                  — Name, Tier, RequiresNetwork, EvaluateAsync
    GhostScorer.cs                   — aggregates signals → Neutral / Elevated / High
    ServiceRegistration.cs           — AddEnvoyGhostDetection() (reflection auto-discovery)
  Envoy.Discovery/           NEW — sanctioned job discovery (public ATS APIs + Brave search)
    Sources/                     — Greenhouse, Lever, Ashby, Workable, Recruitee, Brave
    JobDiscoveryService.cs       — aggregates public postings, ghost-scores them
    ServiceRegistration.cs       — AddEnvoyDiscovery()
  Envoy.UI/                  WPF views (incl. Find Jobs + Apply ghost-risk panel), app host
  Envoy.Assets/              PDF generation, fonts
  Envoy.Templates/           JSON templates for supported job boards
tests/
  Envoy.Core.Tests/            — xUnit, NO network calls
  Envoy.GhostDetection.Tests/  — xUnit, NO network calls
    fixtures/posting-*.json      — labeled sample job postings
  Envoy.Discovery.Tests/       — xUnit + stubbed HttpClient, NO network calls
docs/
  ADAPTIVE_PARSER.md
  TEMPLATE_AUTHORING.md
  SETUP.md
```

## How to Add a Ghost Signal

> **Fast path:** Use the agent-contribution funnel in [`SIGNAL_AUTHORING.md`](SIGNAL_AUTHORING.md). Pick an open [`signal:` issue](https://github.com/LXBStudioLLC/envoy/issues?q=is%3Aissue+label%3Asignal), copy the prompt verbatim, hand it to your coding agent, review the diff, PR. The runbook bakes in the interface contract, precision rules, reference signal, and definition of done.

### Manual checklist

1. **Create a class** in `src/Envoy.GhostDetection/Signals/<Name>Signal.cs` that implements `IGhostSignal` (`Name`, `Tier`, `RequiresNetwork`, `EvaluateAsync`). Set `RequiresNetwork` to `true` only if `EvaluateAsync` makes a network call.
2. **Set `Tier`** appropriately:
   - `Deterministic` — hard evidence (e.g. ATS says closed, scam regex match)
   - `Probabilistic` — strong statistical/correlational evidence
   - `Weak` — noisy signal, contributes to evidence only (near-zero weight)
3. **Implement `EvaluateAsync(JobPosting)`**:
   - Return a `SignalResult` when you have an opinion.
   - Return `null` when you can't evaluate (missing data, unsupported source, timeout).
   - Never throw — catch and return `null`.
4. **Data sources**: public APIs only, no auth scraping. Use short timeouts. Cache responses.
5. **Add fixtures**: create 1–2 JSON files in `tests/Envoy.GhostDetection.Tests/fixtures/` with labeled sample postings.
6. **Add xUnit tests**: mock `HttpClient` or external dependencies — **NO network calls in tests**.
7. **Registration is automatic**: `ServiceRegistration.AddEnvoyGhostDetection()` discovers all `IGhostSignal` implementations via reflection. Network signals with `HttpClient` constructors are auto-registered too. No manual wiring needed.
8. **Open a PR** against `main` from a `feature/<name>` branch. Include test plan and fixture samples.

## Style

- File-scoped namespaces everywhere.
- `ConfigureAwait(false)` is intentionally omitted in `Envoy.Core` — it's consumed in-process by WPF.
- Static frozen brushes live in `src/Envoy.UI/Theme.cs`. Don't redeclare `Color.FromRgb(...)` per view.
- Use `Microsoft.Extensions.Logging` (ILogger<T>) for diagnostics, not `Debug.WriteLine`.
