# Envoy — Agent Instructions

## Product Framing (Current Pivot)

Envoy is a **.NET 8 WPF Windows desktop app** whose **centerpiece is ghost-job detection**: scoring how likely a job posting is a waste of an applicant's time, with **transparent evidence** (risk score + confidence + human-readable reasons).

The existing resume-tailoring + form-fill flow remains as a **human-gated copilot**. There is **no fully-autonomous batch-apply loop** and **no CAPTCHA solving**. The app detects CAPTCHAs and hands off to the human — nothing more.

## Hard Constraints (Never Violate)

1. **NO CAPTCHA-solving, NO bot-detection bypass, NO anti-fingerprinting/evasion code.**
2. Ghost detection only **READS public, sanctioned data**: public ATS JSON APIs, public government datasets, and the posting already in front of the user. **NO scraping behind authentication. NO LinkedIn scraping.**
3. Ghost detection outputs **RISK SCORE + CONFIDENCE + EVIDENCE**. **NEVER** a binary "FAKE"/"GHOST" verdict on a named company. Default to neutral; only flag high on strong (deterministic) evidence or multiple converging weaker signals.
4. **Bias for PRECISION over recall**: flagging a real job is worse than missing a ghost. When unsure, do not flag.
5. Do **NOT** add any autonomous batch-apply loop or CAPTCHA-solver hookpoint.
6. This work is **ADDITIVE**. Do **NOT** refactor the existing apply/tailoring flow beyond confirming gated mode is the default.
7. **.NET 8**, nullable enabled, file-scoped namespaces, match existing patterns. Do **NOT** blanket-add `ConfigureAwait(false)` — `Envoy.Core` is consumed in-process by the WPF UI.
8. Work on branch `feat/ghost-detection` (or `feature/<name>`). Do **NOT** push to or modify `main`. Do **NOT** force-push.

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
    Signals/
      AtsCrossCheckSignal.cs       — reference signal (Deterministic, public ATS APIs)
      PermFilingSignal.cs          — stub
      DuplicateJdSignal.cs         — stub
      PostingAgeSignal.cs          — stub
      RepostFrequencySignal.cs     — stub
      HiringFreezeSignal.cs        — stub
      ScamPatternSignal.cs         — stub
    IGhostSignal.cs
    GhostScorer.cs
    ServiceRegistration.cs
  Envoy.UI/                  WPF views, themes, app host (entry point)
  Envoy.Assets/              PDF generation, fonts
  Envoy.Templates/           JSON templates for supported job boards
tests/
  Envoy.Core.Tests/
  Envoy.GhostDetection.Tests/  — xUnit + Moq, NO network calls
fixtures/
  posting-*.json               — labeled sample job postings
docs/
  ADAPTIVE_PARSER.md
  TEMPLATE_AUTHORING.md
  SETUP.md
```

## How to Add a Ghost Signal

1. **Create a class** in `src/Envoy.GhostDetection/Signals/<Name>Signal.cs` that implements `IGhostSignal`.
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
7. **Registration is automatic**: `ServiceRegistration.AddEnvoyGhostDetection()` discovers all `IGhostSignal` implementations via reflection. No manual registration needed.
8. **Open a PR** against `feat/ghost-detection` (not `main`). Include test plan and fixture samples.

## Style

- File-scoped namespaces everywhere.
- `ConfigureAwait(false)` is intentionally omitted in `Envoy.Core` — it's consumed in-process by WPF.
- Static frozen brushes live in `src/Envoy.UI/Theme.cs`. Don't redeclare `Color.FromRgb(...)` per view.
- Use `Microsoft.Extensions.Logging` (ILogger<T>) for diagnostics, not `Debug.WriteLine`.
