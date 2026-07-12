# Setup Guide

## Requirements

- **Windows 10/11 (64-bit).** Envoy is a WPF desktop application; macOS/Linux are not supported at v1.
- [Ollama](https://ollama.com) installed and running (only required if you want to use a local model; you can run Envoy with a cloud provider instead).
- Google Chrome or Microsoft Edge installed.

## Installation

### Inno Setup installer (recommended)
1. Download `Envoy-<version>-setup.exe` from [Releases](../../releases).
2. Run the installer.
3. Launch Envoy from the Start Menu or desktop shortcut.

### ZIP package
1. Download `Envoy-<version>-win-x64.zip` from [Releases](../../releases).
2. Extract anywhere (e.g. `C:\Tools\Envoy\`).
3. Double-click `Envoy.exe`.

### PowerShell installer
From inside the extracted ZIP, run:
```powershell
.\install.ps1
```
This copies the app to `%LOCALAPPDATA%\Envoy`, adds desktop + Start Menu shortcuts, and registers an uninstall entry under Apps & Features.

## First Launch

1. **Hardware Check:** Envoy detects your GPU and recommends the best local model.
2. **LLM Setup:** Open the LLM Settings view. If Ollama is running, pick a local model. Otherwise enter an OpenAI / Anthropic / Gemini API key and pick a cloud model. Keys are encrypted with Windows DPAPI before being persisted.
3. **Chrome Setup:** Envoy will request you relaunch Chrome with remote debugging enabled.

## GPU Recommendations (local Ollama)

| VRAM | Recommended Model | Speed |
|------|------------------|-------|
| 24GB+ | GLM-5.1 (Q4) | Instant |
| 12-16GB | Qwen2.5-Coder-14B | Fast |
| 8GB | Gemma 4-9B | Good |
| 4-8GB | Llama 3.1-8B (Q3) | Moderate |
| CPU Only | Llama 3.1-8B (Q3) | Slow |

## Chrome Remote Debugging

For the apply copilot's browser bridge to work, Chrome must be launched with:

```
--remote-debugging-port=9222
```

Envoy handles this automatically and relaunches Chrome with the right flag if a debug session isn't already running.

## Troubleshooting

**"Cannot connect to Ollama"**
- Ensure Ollama is running: `ollama serve`
- Check firewall settings on port 11434.
- Or switch to a cloud provider in LLM Settings if you don't want a local model.

**"Cannot connect to Chrome"**
- Close all Chrome windows.
- Let Envoy relaunch Chrome, or launch manually with `--remote-debugging-port=9222`.

**"Model too slow"**
- Open LLM Settings and select a smaller local model, or switch to a cloud provider.
- Ensure your GPU drivers are up to date.

**"Cloud API key not accepted after I copied my settings to another PC"**
- API keys are encrypted with Windows DPAPI under the `CurrentUser` scope, so they cannot be decrypted by a different Windows user account or on a different machine. Re-enter the key on the new machine.
