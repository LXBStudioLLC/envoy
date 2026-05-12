# Envoy

**Your local, sovereign job application agent.**

Envoy is a privacy-first Windows desktop application that tailors your resume for each job and fills out application forms using your own local AI — your data stays on your machine.

## Features

- **Zero-Interaction Resume Parsing:** Drop a PDF. Envoy extracts and structures it automatically using local LLMs.
- **AI-Powered Tailoring:** Every resume is rewritten to match the specific job description.
- **Real Browser Stealth:** Connects to your actual Chrome/Edge via CDP. No bundled browser, no detection.
- **Human Behavior Emulation:** Randomized mouse paths, natural typing cadence, realistic scroll patterns.
- **Safety Guardrails:** Multi-layer validation detects hallucinations, keyword stuffing, and date inconsistencies. Falls back to Safe Mode automatically.
- **Site Templates:** Modular JSON templates for LinkedIn, Greenhouse, Workday, Lever, and Indeed. Community updatable.
- **Adaptive Parser:** Self-healing element locator uses structural fingerprints to recover from DOM changes before falling back to Safe Mode.

## Local by default. Cloud is opt-in.

Envoy runs entirely on your machine using Ollama and a local LLM — that's the default and the recommended posture. If you'd rather use a hosted model, Envoy can talk to OpenAI, Anthropic, or Google Gemini instead. API keys you supply are encrypted with Windows DPAPI under your user account before being written to disk, and cloud calls happen only when you select a cloud provider in LLM Settings.

## Architecture

| Layer | Technology |
|-------|------------|
| **UI** | .NET 8 WPF + HandyControl |
| **Database** | SQLite + EF Core |
| **Local LLM** | Ollama via [OllamaSharp](https://github.com/awaescher/OllamaSharp) |
| **Cloud LLM (optional)** | OpenAI / Anthropic / Gemini via raw HTTP |
| **PDF Parsing** | PdfPig + local LLM post-processor |
| **PDF Generation** | QuestPDF |
| **Browser** | Raw WebSocket → Chrome DevTools Protocol |
| **Behavior** | Custom C# humanization engine |

## Safety First

1. **Parsing Validation:** Regex guards, date consistency, cross-field logic checks.
2. **Tailoring Guardrails:** Factual integrity diff, keyword density cap, one-page constraint.
3. **Pre-Submission Verification:** Field-type matching, template completeness, screenshot audit.
4. **Runtime Anomaly Detection:** CAPTCHA detection, 403/blocked detection, DOM drift detection.

If any layer triggers an anomaly, Envoy **falls back to Safe Mode** — the form is filled, but the agent stops before Submit and asks you to review.

## Requirements

- **Windows 10/11 (64-bit).** Envoy is a WPF desktop application; macOS/Linux are not supported.
- **Ollama** installed and running locally (skip if you only intend to use a cloud provider).
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
5. Envoy handles the rest.

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

### Windows (PowerShell Installer)
Extract the ZIP and run:
```powershell
.\install.ps1
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for build instructions, branch conventions, and how to add a new job-board template. The fastest way to help out is authoring or improving site templates — see [docs/TEMPLATE_AUTHORING.md](docs/TEMPLATE_AUTHORING.md).

## License

[AGPLv3](LICENSE). Your data stays on your machine.

## Roadmap

- [x] Windows ZIP package
- [x] Windows installer (Inno Setup + PowerShell)
- [x] Workday, Lever, Indeed, Greenhouse, LinkedIn templates
- [x] Vault UI for profile history and corrections
- [x] Adaptive parser with self-healing element locator
- [x] Cloud LLM providers (OpenAI, Anthropic, Gemini) — opt-in, DPAPI-encrypted keys
- [ ] Community template marketplace
- [ ] Multi-resume / multi-profile workflows
