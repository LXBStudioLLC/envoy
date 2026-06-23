# Changelog

All notable changes to Envoy are documented in this file. Format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); the project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] — 2026-06-23

Go-live release. Ghost-job detection is now a first-class, in-app feature, and Envoy can discover jobs from sanctioned public sources.

### Added
- **Ghost detection wired into the app.** `AddEnvoyGhostDetection()` runs at startup; every prepared application shows a **Ghost Risk** panel — risk band + confidence + human-readable evidence — in the Apply view.
- **Find Jobs view + `Envoy.Discovery` module.** Sanctioned job discovery that reads **public, unauthenticated ATS board APIs** (Greenhouse, Lever, Ashby, Workable, Recruitee) and an **official, key-gated web-search API** (Brave Search — you supply your own key, stored DPAPI-encrypted). No scraping behind authentication, no anti-bot evasion, no CAPTCHA bypass. Every discovered posting is ghost-scored and shown with a risk badge.
- **Scam Pattern signal** (Deterministic, local regex): flags off-platform interview redirects (Telegram/WhatsApp), upfront fee / PII asks, crypto / gift-card payment demands, and check / overpayment fraud. Precision-first; evidence describes the pattern, never a verdict on a named company.
- `IGhostSignal.RequiresNetwork` so callers can request fast, local-only scoring when ranking many postings at once (e.g. the Find Jobs list).

### Changed
- **Human-gated submit is now truly blocking.** The final submit click waits for an explicit Confirm / Cancel decision in every execution mode; the default Operation Mode is now **Safe**. (Stealth input behavior is unchanged.)
- Removed the inert `HiringFreezeSignal` and `PermFilingSignal` stubs from the shipped build; they remain tracked as future signals (issues #5 and #1). Envoy now ships **5 working signals** — ATS Cross-Check, Posting Age, Duplicate JD, Repost Frequency, Scam Pattern — none inert.

## [0.2.0-beta] — 2026-06-08

Ghost-job detection preview.

### Added
- `Envoy.GhostDetection` signal framework: `IGhostSignal`, `SignalResult`, `GhostScore`, and `GhostScorer` with Neutral / Elevated / High banding (bias for precision over recall).
- Signals: ATS Cross-Check (Deterministic, public Greenhouse/Lever APIs), Posting Age (Probabilistic), Duplicate JD (Weak), Repost Frequency (Weak).
- Fixture-backed xUnit tests with no network calls; reflection-based signal auto-registration.

## [0.1.0] — 2026-05-12

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

[Unreleased]: https://github.com/LXBStudioLLC/envoy/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/LXBStudioLLC/envoy/releases/tag/v1.0.0
[0.2.0-beta]: https://github.com/LXBStudioLLC/envoy/releases/tag/v0.2.0-beta
