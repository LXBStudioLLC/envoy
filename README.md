# Envoy

**Your local, sovereign job application agent.**

Envoy is a fully automated, privacy-first desktop application that tailors your resume for each job and fills out application forms using your own local AI — no cloud, no data leakage, no subscription.

## Features

- **Zero-Interaction Resume Parsing:** Drop a PDF. Envoy extracts and structures it automatically using local LLMs.
- **AI-Powered Tailoring:** Every resume is rewritten to match the specific job description.
- **Real Browser Stealth:** Connects to your actual Chrome/Edge via CDP. No bundled browser, no detection.
- **Human Behavior Emulation:** Randomized mouse paths, natural typing cadence, realistic scroll patterns.
- **Safety Guardrails:** Multi-layer validation detects hallucinations, keyword stuffing, and date inconsistencies. Falls back to Safe Mode automatically.
- **Site Templates:** Modular JSON templates for LinkedIn, Greenhouse, Workday, and more. Community updatable.
- **Cross-Platform:** Built with .NET 10 MAUI. Windows, macOS, Linux.

## Architecture

| Layer | Technology |
|-------|------------|
| **UI** | .NET 10 MAUI Blazor Hybrid + Fluent UI |
| **Database** | SQLite + EF Core |
| **Local LLM** | Ollama via `Microsoft.Extensions.AI` |
| **Orchestration** | Semantic Kernel |
| **PDF Parsing** | PdfPig + Local LLM post-processor |
| **PDF Generation** | QuestPDF |
| **Browser** | Raw WebSocket → Chrome CDP |
| **Behavior** | Custom C# humanization engine |

## Safety First

1. **Parsing Validation:** Regex guards, date consistency, cross-field logic checks.
2. **Tailoring Guardrails:** Factual integrity diff, keyword density cap, one-page constraint.
3. **Pre-Submission Verification:** Field-type matching, template completeness, screenshot audit.
4. **Runtime Anomaly Detection:** CAPTCHA detection, 403/blocked detection, DOM drift detection.

If any layer triggers an anomaly, Envoy **falls back to Safe Mode** — the form is filled, but the agent stops before Submit and asks you to review.

## Requirements

- **Ollama** installed and running locally.
- **Google Chrome** or **Microsoft Edge** installed.
- **GPU recommended:** 8GB+ VRAM for best experience. CPU-only mode supported with smaller models.

## Quick Start (Development)

1. Install Ollama and pull your chosen model:
   ```bash
   ollama pull qwen2.5-coder:14b
   ```
2. Run: `dotnet run --project src/Envoy.UI`
3. Open http://localhost:5000
4. Drop your resume PDF.
5. Paste a job URL.
6. Envoy handles the rest.

## Packaging & Distribution

### Windows (ZIP)
```powershell
.\publish.ps1
```
Output: `artifacts/Envoy-v1.0.0-win-x64.zip` (~50MB)

### Windows (Installer)
1. Install [Inno Setup](https://jrsoftware.org/isdl.php)
2. Compile `setup.iss`
3. Output: `artifacts/Envoy-v1.0.0-setup.exe`

### Windows (PowerShell Installer)
Extract the ZIP and run:
```powershell
.\install.ps1
```

## License

AGPLv3. Your data stays on your machine.

## Roadmap

- [x] Windows ZIP package
- [x] Windows installer script
- [ ] macOS & Linux installers
- [x] Workday, Lever, Indeed templates
- [x] Vault UI for profile history and corrections
- [ ] Community template marketplace
