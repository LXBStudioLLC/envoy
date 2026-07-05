# Security Policy

## Supported versions

Envoy is in active development. Security fixes go out against the latest released version and on the `main` branch. Older releases get fixes only on a best-effort basis.

| Version | Supported |
|---------|-----------|
| 1.0.x (current stable) | Yes |
| < 1.0   | No |

## Reporting a vulnerability

Please don't file public GitHub issues for security vulnerabilities. Instead:

1. Preferred: use [GitHub Security Advisories](https://github.com/LXBStudioLLC/envoy/security/advisories/new) on this repo and click "Report a vulnerability." That keeps the disclosure private until there's a fix.
2. Alternative: email `LXBStudioLLC@gmail.com` with the subject line `[envoy security]`.

Please include:
- A description of the vulnerability and its impact.
- Step-by-step reproduction.
- Affected version(s) or commit SHA.
- Any suggested mitigations.

We'll acknowledge receipt within 7 days and aim to have a fix or mitigation in the next release.

## What's in scope

- The Envoy desktop application source code (`src/`).
- The Inno Setup and PowerShell installers (`setup.iss`, `install.ps1`).
- The cloud LLM provider integrations (OpenAI, Anthropic, Gemini).
- Local data handling (SQLite database, `settings.json`, crash logs).

## What's out of scope

- Vulnerabilities in Ollama, OllamaSharp, HandyControl, QuestPDF, PdfPig, EF Core, or other upstream dependencies. Please report those to the respective projects.
- Vulnerabilities that need an attacker to already run code as your Windows user account.
- Social-engineering issues with the job sites Envoy talks to.

## How we handle cloud LLM API keys

API keys you enter for OpenAI, Anthropic, or Gemini are encrypted with Windows DPAPI under the `DataProtectionScope.CurrentUser` scope before they're written to `%LOCALAPPDATA%\Envoy\settings.json`. That means:

- The ciphertext is only decryptable by the same Windows user account on the same machine.
- Copying `settings.json` to another machine, or another user on the same machine, will silently fail to decrypt. The key field becomes empty until you re-enter it.
- Anyone with admin access on the machine, or the ability to run code as your user, can decrypt the keys. DPAPI is not a defense against a fully compromised host.
- Keys are stored as base64-encoded DPAPI blobs. Nothing on disk is plaintext.

If you find a way to leak a stored key from `settings.json` to another process, user, or machine without your interaction, please report it through the process above.

## Disclosure

After a fix ships, we'll publish a GitHub Security Advisory and credit the reporter, unless they'd rather stay anonymous.

## Repository integrity: is this repo safe to open?

Recent supply-chain attacks have planted auto-executing configuration in repos to target IDEs and AI coding agents (editor task files or agent hooks that run on open). Envoy stays free of those, and we say so plainly so you can check:

- Nothing runs when you open or clone this repo. There's no `.vscode/tasks.json` or any auto-run task, no `.claude/` hooks, no `.cursor/` auto-run rules, and no IDE or shell startup hooks anywhere in the tree.
- No install-time code execution. Envoy is an SDK-style .NET project. It has none of the npm or PyPI-style lifecycle scripts (`preinstall`, `postinstall`, and so on) those attacks abuse, and `dotnet restore` runs no package scripts.
- Building does run code, by definition. `dotnet build` and `dotnet test` compile and run the project's own source and tests. Evaluating an untrusted fork or PR branch? Build it in a throwaway VM or container, not on a machine holding your credentials.
- You can confirm all of this from the diff. Every change lands through a reviewed pull request with required CI. We don't merge auto-run config, build hooks, or obfuscated binaries. Commit author and date are not proof of authorship, so review what a change actually does.

Find an auto-executing file or hook here? Treat it as a security report and use the disclosure process above.
