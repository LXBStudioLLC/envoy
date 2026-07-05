# Envoy

**Ghost-job detection for job seekers. Your data stays local.**

Envoy is a privacy-first Windows desktop application that **scores how likely a job posting is a waste of your time** — with transparent evidence: a risk score, confidence level, and human-readable reasons. It reads only public, sanctioned data (company ATS feeds, optional official web search, the posting already in front of you) and never makes a binary "fake" verdict on a named company.

The resume-tailoring + form-fill assistant is a **human-gated copilot** today — Envoy prepares your application, and you review and submit it. Fuller automation is on the roadmap, and by design it only ever runs on **employer-owned and ATS-hosted career sites**; aggregators like LinkedIn and Indeed stay copilot-only. Envoy never solves CAPTCHAs — if a site challenges it, control returns to you.

## Download (beta)

**[⬇ Download the latest release](https://github.com/LXBStudioLLC/envoy/releases/latest)** — Windows 10/11, 64-bit.

- **Installer** (`Envoy-<version>-setup.exe`) — recommended. Digitally signed by **LXBSTUDIO LLC** (Azure Trusted Signing).
- **Portable ZIP** (`Envoy-<version>-win-x64.zip`) — extract anywhere and run `Envoy.exe`; nothing to install.
- **No .NET runtime required** — the build is self-contained.

Every release ships a `SHA256SUMS.txt` so you can verify your download. The installer is signed, but because the app is new, Windows SmartScreen may still prompt the first few times while the signature builds reputation — the publisher will read **LXBSTUDIO LLC**, and the checksum confirms the rest.

> **Zero-setup:** ghost detection and the **Find Jobs** view work immediately — no LLM, no Ollama, no API key. A local model (Ollama) is only needed for the resume-tailoring copilot.

## Ghost Detection

Envoy analyzes job postings through an extensible **signal framework**:

- **Deterministic signals** — hard evidence (e.g. the role is listed on an aggregator but closed on the company's own ATS, or the description contains a textbook scam pattern).
- **Probabilistic signals** — strong correlational evidence (e.g. the posting has been live far longer than is typical for its seniority).
- **Weak signals** — noisy indicators that add to the evidence list (e.g. the description is a near-duplicate of another company's post, or the same role is repeatedly reposted unchanged).

**Five signals ship today, all wired into the running app and unit-tested:**

| Signal | Tier | Data |
|---|---|---|
| **ATS Cross-Check** | Deterministic | Network — public Greenhouse / Lever ATS APIs |
| **Posting Age** | Probabilistic | Local |
| **Duplicate JD** | Weak | Local |
| **Repost Frequency** | Weak | Local |
| **Scam Pattern** | Deterministic | Local regex — off-platform redirects, upfront fee/PII asks, crypto/gift-card payment, check/overpayment fraud |

Each `IGhostSignal` declares whether it `RequiresNetwork`, so callers can request local-only scoring when ranking many postings at once (e.g. the Find Jobs view) and reserve network calls for a closer look.

Every signal returns **score + confidence + evidence lines**. The aggregator (`GhostScorer`) combines them into a risk band:

| Band | Meaning | When it triggers |
|---|---|---|
| **Neutral** | No strong ghost signals. | Default when signals are absent or weak. |
| **Elevated** | Multiple converging signals suggest caution. | 2+ probabilistic signals scoring ≥ 0.60. |
| **High** | Strong deterministic evidence this posting may be a waste of time. | A deterministic signal scoring ≥ 0.80 with confidence ≥ 0.70. |

**Bias for precision over recall**: flagging a real job is worse than missing a ghost. When unsure, Envoy does not flag.

### Add a signal in 15 minutes (hand it to your agent)

Envoy's signal framework is designed for **agent-driven contribution**: pick an open [`signal:` issue](https://github.com/LXBStudioLLC/envoy/issues?q=is%3Aissue+label%3Asignal), copy the prompt from [`SIGNAL_AUTHORING.md`](SIGNAL_AUTHORING.md), paste it into Claude Code / Kimi / Copilot, review the diff, and PR. The framework auto-discovers every `IGhostSignal` implementation at runtime — zero wiring, zero DI registration. Network signals needing `HttpClient` are auto-registered too.

- **Reference signal**: [`AtsCrossCheckSignal`](src/Envoy.GhostDetection/Signals/AtsCrossCheckSignal.cs) — Greenhouse/Lever cross-check
- **Dogfood example**: [`PostingAgeSignal`](src/Envoy.GhostDetection/Signals/PostingAgeSignal.cs) — built by following the runbook verbatim
- **Shipped today**: ATS cross-check, posting age, duplicate JD, repost frequency, scam patterns
- **Open future lanes**: [hiring freeze](https://github.com/LXBStudioLLC/envoy/issues/5) and [PERM filings](https://github.com/LXBStudioLLC/envoy/issues/1) — grab the issue and hand the prompt to your agent

## Find Jobs

Envoy ships a sanctioned **job-discovery layer** (`Envoy.Discovery`), surfaced in the **Find Jobs** view. It reads only **public, unauthenticated** sources:

- **Public ATS board APIs** — Greenhouse, Lever, Ashby, Workable, and Recruitee.
- **Optional web search** — the official, key-gated **Brave Search** API. You supply your own key, stored DPAPI-encrypted in settings; if you don't, discovery falls back to the ATS feeds alone.

**Every discovered posting is ghost-scored** before it reaches you, so risk bands and evidence appear right in the results list. There is **no scraping behind authentication, no anti-bot evasion, and no CAPTCHA bypass** — discovery stays within publicly sanctioned endpoints.

## Human-Gated Copilot

Envoy can also help you apply to individual jobs:

- **Zero-Interaction Resume Parsing:** Drop a PDF. Envoy extracts and structures it automatically using local LLMs.
- **AI-Powered Tailoring:** Every resume is rewritten to match the specific job description.
- **Safety Guardrails:** Multi-layer validation detects hallucinations, keyword stuffing, and date inconsistencies.
- **Site Templates:** Modular JSON templates for common job boards. Community updatable.
- **Adaptive Parser:** Self-healing element locator uses structural fingerprints to recover from DOM changes.
- **Ghost Risk panel:** The Apply view shows the posting's risk band, confidence, and evidence before you invest time tailoring a resume.

**Before any form is submitted**, the submit click is **blocking in every execution mode** — Envoy fills the form, then stops and waits for your explicit **Confirm** or **Cancel**. The default Operation Mode is **Safe**. (The optional Stealth mode only changes *how* text is typed; it never bypasses the submit confirmation.)

> **Note:** Envoy requires your explicit review before every submission today. Fuller automation on the roadmap stays scoped to employer-owned and ATS-hosted career sites — never aggregators like LinkedIn or Indeed.

## Local by default. Cloud is opt-in.

Envoy runs entirely on your machine using Ollama and a local LLM — that's the default and the recommended posture. If you'd rather use a hosted model, Envoy can talk to OpenAI, Anthropic, or Google Gemini instead. API keys you supply are encrypted with Windows DPAPI under your user account before being written to disk, and cloud calls happen only when you select a cloud provider in LLM Settings.

## Architecture

| Layer | Technology |
|-------|------------|
| **UI** | .NET 8 WPF + HandyControl |
| **Database** | SQLite + EF Core |
| **Ghost Detection** | Extensible signal framework (Deterministic / Probabilistic / Weak tiers) |
| **Job Discovery** | `Envoy.Discovery` — public ATS board feeds (Greenhouse, Lever, Ashby, Workable, Recruitee) + optional Brave Search |
| **Local LLM** | Ollama via [OllamaSharp](https://github.com/awaescher/OllamaSharp) |
| **Cloud LLM (optional)** | OpenAI / Anthropic / Gemini via raw HTTP |
| **PDF Parsing** | PdfPig + local LLM post-processor |
| **PDF Generation** | QuestPDF |
| **Browser** | Raw WebSocket → Chrome DevTools Protocol |
| **Form Fill** | Plain synthetic input by default; optional human-cadence typing/mouse mode — never bypasses the submit confirmation |

## Requirements

- **Windows 10/11 (64-bit).** Envoy is a WPF desktop application; macOS/Linux are not supported.
- **Ollama** *(optional)* — only for the resume-tailoring copilot. Ghost detection and Find Jobs need no LLM at all. Skip it if you'll use a cloud provider or only want ghost detection.
- **Google Chrome** or **Microsoft Edge** installed.
- **GPU recommended:** 8GB+ VRAM for best local-LLM experience. CPU-only mode supported with smaller models.

## Quick Start (Development)

1. Install Ollama and pull your chosen model:
   ```powershell
   ollama pull qwen2.5-coder:14b
   ```
2. Run the app — this opens a Windows desktop window:
   ```powershell
   dotnet run --project src/Envoy.UI
   ```
3. Drop your resume PDF on the Dashboard.
4. Paste a job URL.
5. Envoy prepares the application — you review and submit.

## Packaging & Distribution

### Windows (ZIP)
```powershell
.\publish.ps1
```
Output: `artifacts/Envoy-v1.0.0-win-x64.zip`

### Windows (Inno Setup Installer)
1. Install [Inno Setup](https://jrsoftware.org/isdl.php)
2. Compile `setup.iss`
3. Output: `artifacts/Envoy-v1.0.0-setup.exe`

### Windows (PowerShell installer, from a clone)
After building the ZIP, install to `%LOCALAPPDATA%\Envoy` with shortcuts:
```powershell
.\install.ps1
```
The portable ZIP itself contains `Envoy.exe` and `Start Envoy.bat` — just extract and run, no installer needed.

## Contributing

The fastest way to help out is **authoring a ghost signal**. See [CONTRIBUTING.md](CONTRIBUTING.md) and [AGENTS.md](AGENTS.md) for the step-by-step guide. The framework auto-discovers signals at runtime — no manual registration needed.

## Feedback & bug reports

Found a bug or a false flag? [Open an issue](https://github.com/LXBStudioLLC/envoy/issues/new/choose). A good report includes your Envoy version, your Windows version, and — if it crashed — the log at `%LOCALAPPDATA%\Envoy\crash.log` (redact anything personal first). Envoy is precision-first, so a **real** job flagged as a possible ghost is the worst-case outcome — those reports are especially valuable.

## License

[AGPLv3](LICENSE). Your data stays on your machine.

## Roadmap

- [x] Ghost-job detection signal framework (wired into the running app)
- [x] Five shipped signals: ATS cross-check, posting age, duplicate JD, repost frequency, scam pattern
- [x] Job discovery via public ATS feeds + optional Brave Search, every posting ghost-scored
- [ ] Future signals: hiring freeze ([#5](https://github.com/LXBStudioLLC/envoy/issues/5)), PERM filings ([#1](https://github.com/LXBStudioLLC/envoy/issues/1))
- [x] Windows ZIP package
- [x] Windows installer (Inno Setup + PowerShell)
- [x] Workday, Lever, Indeed, Greenhouse, LinkedIn templates
- [x] Vault UI for profile history and corrections
- [x] Adaptive parser with self-healing element locator
- [x] Cloud LLM providers (OpenAI, Anthropic, Gemini) — opt-in, DPAPI-encrypted keys
- [ ] Full-auto apply for employer-owned & ATS career sites (aggregators always stay copilot-only)
- [ ] Multi-resume / multi-profile workflows
