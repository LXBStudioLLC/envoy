---
name: Bug report
about: Something is broken or behaves unexpectedly
title: "[bug] "
labels: bug
---

## What happened

A clear description of the bug.

## What you expected

What you thought would happen instead.

## Reproduction steps

1.
2.
3.

## Environment

- Envoy version (Settings → About, or installer filename):
- Windows version (`winver`):
- .NET SDK (`dotnet --version`, only if running from source):
- LLM provider (Ollama / LM Studio / OpenAI / Anthropic / Gemini):
- Model:
- GPU / VRAM (if local):
- Browser (Chrome / Edge + version):

## Crash log

If the app crashed, paste the relevant section from `%LOCALAPPDATA%\Envoy\crash.log` (redact any personal info).

```
<paste here>
```

## Relocation log (parser issues only)

If this is a "field not found" or wrong-field issue, paste the last few lines of `%LOCALAPPDATA%\Envoy\relocations.jsonl` showing the failed step.

```
<paste here>
```

## Screenshots

If applicable, attach screenshots — redact any personal info, the job URL, or the company name unless you're comfortable making them public.

## Additional context

Anything else worth knowing.
