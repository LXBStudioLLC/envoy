# Envoy — Agent Instructions

(See [AGENTS.md](../AGENTS.md) in the repo root for the canonical version.)

## Product Framing (Current Pivot)

Envoy is a **.NET 8 WPF Windows desktop app** whose **centerpiece is ghost-job detection**: scoring how likely a job posting is a waste of an applicant's time, with **transparent evidence** (risk score + confidence + human-readable reasons).

The existing resume-tailoring + form-fill flow remains as a **human-gated copilot**. There is **no fully-autonomous batch-apply loop** and **no CAPTCHA solving**. The app detects CAPTCHAs and hands off to the human — nothing more.

## Hard Constraints (Never Violate)

1. **Human-cadence "stealth" input lives behind an explicit, off-by-default, guarded opt-in — never the default, never on the data layers.**
   - The apply/form-fill copilot automates **the user's own actions in their own browser session** and is **human-gated** (the user explicitly confirms before any submit).
   - Human-cadence input emulation (Bezier mouse paths, typing jitter — the "Stealth" input option) is **retained** but stays **OFF by default** (`StealthModeEnabled`) and enables only through a deliberate, guarded toggle in the Browser view behind an acknowledgement. When off, forms are filled with plain input and only Safe mode is offered.
   - **NO CAPTCHA-solving, ever** — detect a CAPTCHA and hand off to the human; no CAPTCHA-solver hookpoint.
   - The **ghost-detection and job-discovery data layers READ ONLY public, sanctioned data** and must **never** use scraping or anti-bot evasion. Stealth input emulation must **never** be repurposed to bypass bot-detection or to harvest data a site withholds.
2. Ghost detection **and job discovery** only **READ public, sanctioned data**: public ATS JSON APIs, official key-gated search APIs (e.g. Brave Search), public government datasets, and the posting already in front of the user. **NO scraping behind authentication. NO LinkedIn scraping. NO defeating a site's bot-protection to access data.**
3. Ghost detection outputs **RISK SCORE + CONFIDENCE + EVIDENCE**. **NEVER** a binary "FAKE"/"GHOST" verdict on a named company. Default to neutral; only flag high on strong (deterministic) evidence or multiple converging weaker signals.
4. **Bias for PRECISION over recall**: flagging a real job is worse than missing a ghost. When unsure, do not flag.
5. Do **NOT** add any autonomous batch-apply loop or CAPTCHA-solver hookpoint.
6. This work is **ADDITIVE**. Do **NOT** refactor the existing apply/tailoring flow beyond confirming gated mode is the default.
7. **.NET 8**, nullable enabled, file-scoped namespaces, match existing patterns. Do **NOT** blanket-add `ConfigureAwait(false)`.
8. Work on a `feature/<name>` branch created off `main`. Do **NOT** push directly to `main`. Do **NOT** force-push.

## Build / Test / Run

```powershell
dotnet restore
dotnet build -c Release
dotnet test
```

## How to Add a Ghost Signal

1. Create `src/Envoy.GhostDetection/Signals/<Name>Signal.cs` implementing `IGhostSignal`.
2. Set `Tier`: Deterministic / Probabilistic / Weak.
3. Implement `EvaluateAsync(JobPosting)`: return `SignalResult` or `null`. Never throw.
4. Use public data only. Short timeouts. Cache responses.
5. Add fixtures in `tests/Envoy.GhostDetection.Tests/fixtures/`.
6. Add xUnit tests with mocked dependencies — **NO network calls in tests**.
7. Registration is automatic via `AddEnvoyGhostDetection()` reflection.
8. Open a PR against `main` from a `feature/<name>` branch.

See [AGENTS.md](../AGENTS.md) for the full module map and architecture.
