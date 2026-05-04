# Setup Guide

## Requirements

- Windows 10/11, macOS 12+, or Linux
- [Ollama](https://ollama.com) installed and running
- Google Chrome or Microsoft Edge

## Installation

### Windows
1. Download `Envoy-Setup.exe` from [Releases](../../releases)
2. Run the installer. It will check for Ollama and offer to install it if missing.
3. Launch Envoy from the Start Menu.

### macOS
1. Download `Envoy.dmg` from [Releases](../../releases)
2. Drag Envoy to Applications.
3. Launch Envoy. Grant permissions when prompted.

### Linux
1. Download `Envoy.AppImage` from [Releases](../../releases)
2. `chmod +x Envoy.AppImage`
3. `./Envoy.AppImage`

## First Launch

1. **Hardware Check:** Envoy will detect your GPU and recommend the best local model.
2. **Ollama Setup:** If Ollama is not running, the app will prompt you to start it.
3. **Chrome Setup:** Envoy will request you relaunch Chrome with remote debugging enabled.

## GPU Recommendations

| VRAM | Recommended Model | Speed |
|------|------------------|-------|
| 24GB+ | GLM-5.1 (Q4) | Instant |
| 12-16GB | Qwen2.5-Coder-14B | Fast |
| 8GB | Gemma 4-9B | Good |
| 4-8GB | Llama 3.1-8B (Q3) | Moderate |
| CPU Only | Llama 3.1-8B (Q3) | Slow |

## Chrome Remote Debugging

For the stealth browser to work, Chrome must be launched with:

```bash
--remote-debugging-port=9222
```

Envoy will handle this automatically on Windows. On macOS/Linux, you may need to close Chrome and let Envoy relaunch it.

## Troubleshooting

**"Cannot connect to Ollama"**
- Ensure Ollama is running: `ollama serve`
- Check firewall settings on port 11434

**"Cannot connect to Chrome"**
- Close all Chrome windows
- Let Envoy relaunch Chrome, or launch manually with `--remote-debugging-port=9222`

**"Model too slow"**
- Go to Settings and select a smaller model
- Ensure your GPU drivers are up to date
