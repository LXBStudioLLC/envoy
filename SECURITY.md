# Security Policy

## Supported versions

Envoy is in active development. Security fixes are issued against the latest released version and on the `main` branch. Older releases receive fixes only on a best-effort basis.

| Version | Supported |
|---------|-----------|
| 1.0.x   | Yes |
| < 1.0   | No |

## Reporting a vulnerability

**Do not file public GitHub issues for security vulnerabilities.** Instead:

1. **Preferred:** Use [GitHub Security Advisories](https://github.com/LXBStudioLLC/envoy/security/advisories/new) on this repository — click "Report a vulnerability." This keeps the disclosure private until we publish a fix.
2. **Alternative:** Email `LXBStudioLLC@gmail.com` with the subject line `[envoy security]`.

Please include:
- A description of the vulnerability and its impact.
- Step-by-step reproduction.
- Affected version(s) / commit SHA.
- Any suggested mitigations.

We'll acknowledge receipt within 7 days and aim to have a fix or mitigation in the next release.

## What's in scope

- The Envoy desktop application source code (`src/`).
- Inno Setup and PowerShell installers (`setup.iss`, `install.ps1`).
- Cloud LLM provider integrations (OpenAI, Anthropic, Gemini).
- Local data handling (SQLite database, `settings.json`, crash logs).

## What's out of scope

- Vulnerabilities in Ollama, OllamaSharp, HandyControl, QuestPDF, PdfPig, EF Core, or other upstream dependencies — please report those to the respective projects.
- Vulnerabilities that require an attacker to already have code execution as your Windows user account.
- Social-engineering issues with job sites Envoy interacts with.

## How we handle cloud LLM API keys

API keys you enter for OpenAI / Anthropic / Gemini are encrypted with **Windows DPAPI** under the `DataProtectionScope.CurrentUser` scope before being persisted to `%LOCALAPPDATA%\Envoy\settings.json`. This means:

- The ciphertext is **only decryptable** by the same Windows user account on the same machine.
- Copying `settings.json` to another machine (or another user account on the same machine) will silently fail to decrypt — the key field becomes empty until you re-enter it.
- Anyone with admin access on the machine, or the ability to run code as your user, can decrypt the keys. DPAPI is not a defense against a fully compromised host.
- Keys are stored as base64-encoded DPAPI blobs; nothing on disk is plaintext.

If you discover a way to leak a stored key from `settings.json` to another process / user / machine without your interaction, please report it under the process above.

## Disclosure

After a fix ships, we'll publish a GitHub Security Advisory crediting the reporter (unless they prefer to remain anonymous).
