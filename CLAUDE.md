# Envoy — Agent Instructions

(See [AGENTS.md](../AGENTS.md) in the repo root for the canonical version.)

## Product Framing

Envoy is a **.NET 8 WPF Windows desktop app** with two pillars:

1. **Ghost-job detection** — scoring how likely a job posting is a waste of an applicant's time, with **transparent evidence** (risk score + confidence + human-readable reasons).
2. **Application automation** — a resume-tailoring + form-fill engine that ranges from a **human-gated copilot** to **full-auto submission**. First-class feature; meant to be improved and extended.

## Constraints

1. **Automation is first-class; make it better, not smaller.** The apply/form-fill engine spans **Copilot** (user confirms each submit) through **Full-Auto** (unattended submission). Human-cadence input emulation (the "Stealth" input option) is a **normal, configurable option** — no forced default, no acknowledgement gate. It automates **the user's own actions with the user's own data**.
2. **Full-Auto is scoped to company-owned and ATS-hosted career sites.** Aggregators (LinkedIn, Indeed, Glassdoor, ZipRecruiter, Monster, Dice, etc.) are **Copilot-only by product choice**, enforced **in code** via a site classifier — not just documented.
3. **On a hard block, hand off — don't fight it.** CAPTCHA, bot-check, or rate-limit → surface to the user and log it. **No CAPTCHA-solving, and no techniques whose purpose is to defeat a site's bot-detection.** Full-Auto fails gracefully and yields to the human.
4. **Data layers read public data and don't bypass authentication.** Ghost detection and job discovery use public ATS JSON APIs, key-gated search APIs (e.g. Brave Search), public government datasets, and the posting in front of the user. **No scraping behind a login.**
5. Ghost detection outputs **RISK SCORE + CONFIDENCE + EVIDENCE** — an assessment with reasons, defaulting to neutral, not a bare "FAKE"/"GHOST" label on a named company. (Kept as the differentiator and defamation cover; precision-vs-recall is a tunable default, not a hard rule.)
6. **.NET 8**, nullable enabled, file-scoped namespaces, match existing patterns. Do **NOT** blanket-add `ConfigureAwait(false)`.
7. Work on a `feature/<name>` branch created off `main`. Do **NOT** push directly to `main`. Do **NOT** force-push.

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
