# Envoy — Agent Instructions

## Product Framing (Current Pivot)

Envoy is a **.NET 8 WPF Windows desktop app** whose **centerpiece is ghost-job detection**: scoring how likely a job posting is a waste of an applicant's time, with **transparent evidence** (risk score + confidence + human-readable reasons).

The existing resume-tailoring + form-fill flow remains as a **human-gated copilot**. There is **no fully-autonomous batch-apply loop** and **no CAPTCHA solving**. The app detects CAPTCHAs and hands off to the human — nothing more.

## Hard Constraints (Never Violate)

1. **Stealth input emulation is allowed ONLY inside the human-gated apply copilot; the data layers stay clean.**
   - The apply/form-fill copilot automates **the user's own actions in their own browser session** and is **human-gated** (the user explicitly confirms before any submit). Human-cadence input emulation — natural typing/mouse timing, the "Stealth" execution mode — is **permitted here** so assistive automation behaves like a human operator.
   - **NO CAPTCHA-solving, ever** — detect a CAPTCHA and hand off to the human; no CAPTCHA-solver hookpoint.
   - The **ghost-detection and job-discovery data layers READ ONLY public, sanctioned data** and must **never** use scraping or anti-bot evasion. Stealth input emulation must **never** be repurposed to bypass bot-detection or to harvest data a site withholds.
2. Ghost detection **and job discovery** only **READ public, sanctioned data**: public ATS JSON APIs, official key-gated search APIs (e.g. Brave Search), public government datasets, and the posting already in front of the user. **NO scraping behind authentication. NO LinkedIn scraping. NO defeating a site's bot-protection to access data.**
3. Ghost detection outputs **RISK SCORE + CONFIDENCE + EVIDENCE**. **NEVER** a binary "FAKE"/"GHOST" verdict on a named company. Default to neutral; only flag high on strong (deterministic) evidence or multiple converging weaker signals.
4. **Bias for PRECISION over recall**: flagging a real job is worse than missing a ghost. When unsure, do not flag.
5. Do **NOT** add any autonomous batch-apply loop or CAPTCHA-solver hookpoint.
6. This work is **ADDITIVE**. Do **NOT** refactor the existing apply/tailoring flow beyond confirming gated mode is the default.
7. **.NET 8**, nullable enabled, file-scoped namespaces, match existing patterns. Do **NOT** blanket-add `ConfigureAwait(false)` — `Envoy.Core` is consumed in-process by the WPF UI.
8. Work on a `feature/<name>` branch created off `main`. Do **NOT** push directly to `main`. Do **NOT** force-push.

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
