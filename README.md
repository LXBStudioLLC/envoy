<p align="center">
  <!-- HERO BANNER SLOT — assets/banner.png (~1280px wide wordmark + tagline). assets/ is currently empty; drop the final in here. -->
  <img src="assets/banner.png" alt="Envoy" width="640">
</p>

<h1 align="center">Envoy</h1>

<p align="center">
  <strong>See through ghost jobs, tailor your resume, and skip the busywork — all on your own PC.</strong><br>
  Evidence, not verdicts. Your data never leaves your machine.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/license-AGPL--3.0-blue.svg" alt="License: AGPL-3.0">
  <img src="https://img.shields.io/badge/release-v1.0.1-2ea44f" alt="Latest release v1.0.1">
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078D6" alt="Platform: Windows 10/11 x64">
  <img src="https://img.shields.io/badge/installer-Authenticode%20signed-4c1" alt="Signed installer">
  <img src="https://img.shields.io/badge/built%20with-.NET%208-512BD4" alt="Built with .NET 8">
</p>

<p align="center">
  <a href="https://github.com/LXBStudioLLC/envoy/releases/latest"><img src="https://img.shields.io/badge/%E2%AC%87%20Download%20Envoy-Windows%2010%2F11%20x64-2ea44f?style=for-the-badge" alt="Download Envoy"></a>
</p>

<p align="center">
  <a href="#-download">Download</a> ·
  <a href="#-ghost-detection">How it works</a> ·
  <a href="#-find-jobs">Find Jobs</a> ·
  <a href="#-apply-copilot">Apply Copilot</a> ·
  <a href="#-privacy">Privacy</a> ·
  <a href="#-for-developers">Contributing</a>
</p>

---

You tailor the resume. You fill the same fields for the tenth time. You hit **submit** — and nothing comes back. Some of those postings were never real openings. **Envoy helps you spot them before you spend an hour on them.**

## TL;DR

- **👻 Ghost-job risk, with receipts.** Envoy scores how likely a posting is a waste of your time — and shows you *why*: a risk score, a confidence level, and plain-English reasons. Never a bare "fake" label on a company; when it's unsure, it stays neutral.
- **🆓 Free, local, zero-setup.** Ghost detection and job discovery work the moment you open the app — no account, no API key, no cloud. Your resume and data stay on your PC.
- **🤖 A copilot that respects the submit button.** Envoy tailors your resume and fills the application, then stops and waits for **you** to review and click submit — in every mode.
- **🪟 Windows 10/11, self-contained.** Signed installer or portable ZIP. No .NET runtime to install.

<p align="center">
  <!-- SCREENSHOT SLOT 1 (hero) — assets/ghost-score.png (~1600px wide): the Ghost Risk panel showing a risk band (Neutral / Elevated / High), the confidence level, and the expanded human-readable evidence lines. This is the most important image — it proves "evidence, not verdict." -->
  <img src="assets/ghost-score.png" alt="Envoy Ghost Risk panel showing risk band, confidence, and evidence lines" width="820">
</p>
<p align="center"><em>The Ghost Risk panel — risk band, confidence, and the reasons behind the score. (Screenshot coming soon.)</em></p>

---

## ⬇️ Download

**[Download the latest release](https://github.com/LXBStudioLLC/envoy/releases/latest)** — Windows 10/11, 64-bit.

| Option | File | Notes |
|---|---|---|
| **Installer** *(recommended)* | `Envoy-<version>-setup.exe` | Digitally signed by **LXBSTUDIO LLC** (Azure Trusted Signing). |
| **Portable ZIP** | `Envoy-<version>-win-x64.zip` | Extract anywhere and run `Envoy.exe` — nothing to install. |

- **No .NET runtime required** — the build is self-contained.
- Every release ships a **`SHA256SUMS.txt`** so you can verify your download.
- The installer is signed, but because the app is new, Windows **SmartScreen** may prompt the first few times while the signature builds reputation. The publisher will read **LXBSTUDIO LLC**, and the checksum confirms the rest.

> 💡 **Zero setup:** ghost detection and **Find Jobs** work immediately — no LLM, no Ollama, no API key. A local model is only needed for the resume-tailoring copilot.

---

## 👻 Ghost Detection

This is the heart of Envoy. It analyzes each posting through an extensible **signal framework**, where every signal returns a **score + confidence + evidence lines** rather than a yes/no verdict.

Signals come in three tiers:

- **Deterministic** — hard evidence (e.g. the role is open on an aggregator but already closed on the company's own ATS, or the text contains a textbook scam pattern).
- **Probabilistic** — strong correlational evidence (e.g. the posting has been live far longer than is typical for its seniority).
- **Weak** — noisy indicators that add to the evidence list (e.g. the description is a near-duplicate of another company's post).

**Four signals are active in the running app, all unit-tested:**

| Signal | Tier | Data |
|---|---|---|
| **ATS Cross-Check** | Deterministic | Network — public Greenhouse / Lever ATS APIs |
| **Posting Age** | Probabilistic | Local — posting date from the discovery feed |
| **Duplicate JD** | Weak | Local — cross-company text match within a discovery batch |
| **Scam Pattern** | Deterministic | Local regex — off-platform redirects, upfront fee/PII asks, crypto/gift-card payment, check/overpayment fraud |

> A fifth signal, **Repost Frequency** (Weak), is implemented and unit-tested but stays **dormant** until Envoy persists listing history across sessions — storage this build doesn't ship yet — so it does not fire.

Each signal declares whether it needs the network, so Envoy can do fast **local-only** scoring when ranking many postings at once (the Find Jobs view) and reserve network calls for a closer look.

### Risk bands

The aggregator (`GhostScorer`) combines every signal's output into one of three bands:

| Band | Meaning | When it triggers |
|---|---|---|
| **Neutral** | No strong ghost signals. | Default when signals are absent or weak. |
| **Elevated** | Multiple converging signals suggest caution. | 2+ probabilistic signals scoring ≥ 0.60. |
| **High** | Strong deterministic evidence this posting may waste your time. | A deterministic signal scoring ≥ 0.80 with confidence ≥ 0.70. |

> **Bias for precision over recall:** flagging a real job is worse than missing a ghost, so when Envoy is unsure it stays **Neutral**. The **Elevated** band becomes reachable once a second probabilistic signal ships; with today's active set (one probabilistic signal, Posting Age), postings resolve to **Neutral** or **High**.

---

## 🔎 Find Jobs

Envoy ships a sanctioned **job-discovery layer** (`Envoy.Discovery`), surfaced in the **Find Jobs** view. It reads only **public, unauthenticated** sources:

- **Public ATS board APIs** — Greenhouse, Lever, Ashby, Workable, and Recruitee.
- **Optional web search** — the official, key-gated **Brave Search** API. Supply your own key (stored DPAPI-encrypted); without it, discovery falls back to the ATS feeds alone.

**Every discovered posting is ghost-scored before it reaches you**, so risk bands and evidence appear right in the results list. There is **no scraping behind authentication, no anti-bot evasion, and no CAPTCHA bypass** — discovery stays within publicly sanctioned endpoints.

<p align="center">
  <!-- SCREENSHOT SLOT 2 — assets/find-jobs.png: the discovery results list with a ghost risk band rendered inline on each posting, showing scoring happens before results reach the user. -->
  <img src="assets/find-jobs.png" alt="Find Jobs results list with a ghost risk band on each posting" width="820">
</p>
<p align="center"><em>Find Jobs — every result carries its own risk band before you click. (Screenshot coming soon.)</em></p>

---

## 🤖 Apply Copilot

Once you've found a posting worth your time, Envoy helps you apply:

- **Drop-in resume parsing** — drop a PDF; Envoy extracts and structures it automatically using a local LLM.
- **AI-powered tailoring** — your resume is rewritten to match the specific job description.
- **Safety guardrails** — multi-layer validation catches hallucinations, keyword stuffing, and date inconsistencies.
- **Site templates** — modular JSON templates for common job boards; community-updatable.
- **Adaptive parser** — a self-healing element locator uses structural fingerprints to recover from DOM changes.
- **Ghost Risk panel** — the Apply view shows the posting's risk band, confidence, and evidence before you invest time tailoring.

**You always press submit.** Envoy fills the form, then stops and waits for your explicit **Confirm** or **Cancel** — the submit click is blocking in every execution mode, and the default Operation Mode is **Safe**. (The optional Stealth mode only changes *how* text is typed; it never bypasses the confirmation.)

> **Scope, stated once:** fuller automation is on the roadmap, and by design it only ever runs on **employer-owned and ATS-hosted career sites** — aggregators like LinkedIn and Indeed stay copilot-only. Envoy never solves CAPTCHAs; if a site challenges it, control returns to you.

<p align="center">
  <!-- SCREENSHOT SLOT 3 — assets/apply-copilot.png: the Apply view showing the Ghost Risk panel alongside a filled form paused at the Confirm / Cancel submit gate, proving the "we stop and wait for you" claim. -->
  <img src="assets/apply-copilot.png" alt="Apply view with a filled form paused at the Confirm / Cancel submit gate" width="820">
</p>
<p align="center"><em>The Apply Copilot pauses at the submit gate — nothing is sent until you confirm. (Screenshot coming soon.)</em></p>

---

## 🔒 Privacy

**Local by default. Cloud is opt-in.** Envoy runs entirely on your machine using Ollama and a local LLM — that's the default and the recommended posture. If you'd rather use a hosted model, Envoy can talk to OpenAI, Anthropic, or Google Gemini instead. Any API keys you supply are encrypted with **Windows DPAPI** under your user account before they're written to disk, and cloud calls happen only when you select a cloud provider in LLM Settings.

## 📋 Requirements

- **Windows 10/11 (64-bit).** Envoy is a WPF desktop app; macOS/Linux are not supported.
- **Ollama** *(optional)* — only for the resume-tailoring copilot. Ghost detection and Find Jobs need no LLM at all.
- **Google Chrome** or **Microsoft Edge** installed (for the Apply Copilot).
- **GPU recommended** — 8GB+ VRAM for the best local-LLM experience; CPU-only mode works with smaller models.

---

## 🛠️ For developers

Envoy is built on **.NET 8 WPF**, and the ghost-signal framework is designed to be **agent-contributable** — you can open a signal file, see exactly why it fired, or write a sharper one and send a PR.

**Author a signal in ~15 minutes:** pick an open [`signal:` issue](https://github.com/LXBStudioLLC/envoy/issues?q=is%3Aissue+label%3Asignal), copy the prompt from [`SIGNAL_AUTHORING.md`](SIGNAL_AUTHORING.md), hand it to your coding agent, review the diff, and open a PR. The framework **auto-discovers** every `IGhostSignal` at runtime — zero wiring, zero DI registration.

- **Reference signal:** [`AtsCrossCheckSignal`](src/Envoy.GhostDetection/Signals/AtsCrossCheckSignal.cs) — Greenhouse/Lever cross-check.
- **Dogfood example:** [`PostingAgeSignal`](src/Envoy.GhostDetection/Signals/PostingAgeSignal.cs) — built by following the runbook verbatim.
- **Open future lanes:** [hiring freeze](https://github.com/LXBStudioLLC/envoy/issues/5) and [PERM filings](https://github.com/LXBStudioLLC/envoy/issues/1).

Full build, architecture, and packaging details live in **[CONTRIBUTING.md](CONTRIBUTING.md)** and **[AGENTS.md](AGENTS.md)**.

<details>
<summary><strong>Build from source</strong></summary>

```powershell
dotnet restore
dotnet build -c Release
dotnet test

# Run the desktop app (opens a Windows window)
dotnet run --project src/Envoy.UI
```

Install Ollama and pull a model first if you want the resume copilot:

```powershell
ollama pull qwen2.5-coder:14b
```

</details>

<details>
<summary><strong>Architecture</strong></summary>

| Layer | Technology |
|-------|------------|
| **UI** | .NET 8 WPF + HandyControl |
| **Database** | SQLite + EF Core |
| **Ghost Detection** | Extensible signal framework (Deterministic / Probabilistic / Weak tiers) |
| **Job Discovery** | `Envoy.Discovery` — public ATS feeds (Greenhouse, Lever, Ashby, Workable, Recruitee) + optional Brave Search |
| **Local LLM** | Ollama via [OllamaSharp](https://github.com/awaescher/OllamaSharp) |
| **Cloud LLM (optional)** | OpenAI / Anthropic / Gemini via raw HTTP |
| **PDF Parsing** | PdfPig + local LLM post-processor |
| **PDF Generation** | QuestPDF |
| **Browser** | Raw WebSocket → Chrome DevTools Protocol |
| **Form Fill** | Plain synthetic input by default; optional human-cadence typing/mouse mode — never bypasses the submit confirmation |

</details>

<details>
<summary><strong>Packaging &amp; release</strong></summary>

**One-command release** (recommended) — reads `<Version>` from `Directory.Build.props` and emits the ZIP, the signed installer, and `SHA256SUMS.txt`:

```powershell
.\build-release.ps1
```

**Manual steps** (alternative):

```powershell
.\publish.ps1     # -> artifacts/Envoy-<version>-win-x64.zip
# Compile setup.iss with Inno Setup -> artifacts/Envoy-<version>-setup.exe
.\install.ps1     # installs the built ZIP to %LOCALAPPDATA%\Envoy with shortcuts
```

The version is sourced from `Directory.Build.props`, so nothing here needs a hardcoded number.

</details>

---

## 🐞 Feedback & bug reports

Found a bug or a false flag? **[Open an issue](https://github.com/LXBStudioLLC/envoy/issues/new/choose).** A good report includes your Envoy version, your Windows version, and — if it crashed — the log at `%LOCALAPPDATA%\Envoy\crash.log` (redact anything personal first). Envoy is precision-first, so a **real** job flagged as a possible ghost is the worst-case outcome — those reports are especially valuable.

## 🗺️ Roadmap

- [x] Ghost-job detection signal framework (wired into the running app)
- [x] **Four active signals:** ATS cross-check, posting age, duplicate JD, scam pattern
- [ ] **Repost frequency signal** — implemented and unit-tested; dormant until cross-session listing history persists
- [x] Job discovery via public ATS feeds + optional Brave Search, every posting ghost-scored
- [ ] Future signals: hiring freeze ([#5](https://github.com/LXBStudioLLC/envoy/issues/5)), PERM filings ([#1](https://github.com/LXBStudioLLC/envoy/issues/1))
- [x] Windows ZIP package + signed installer (Inno Setup + PowerShell)
- [x] Vault UI for profile history and corrections
- [x] Adaptive parser with self-healing element locator
- [x] Cloud LLM providers (OpenAI, Anthropic, Gemini) — opt-in, DPAPI-encrypted keys
- [ ] Full-auto apply for employer-owned & ATS career sites (aggregators always stay copilot-only)
- [ ] Multi-resume / multi-profile workflows

## 📄 License

[AGPL-3.0](LICENSE). Your data stays on your machine.