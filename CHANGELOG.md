# Changelog

All notable changes to Envoy are documented in this file. Format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); the project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] — 2026-05-12

Initial public release.

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
- Adaptive parser with structural-fingerprint element locator and JSONL relocation logging at `%LOCALAPPDATA%\Envoy\relocation.jsonl`.
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
