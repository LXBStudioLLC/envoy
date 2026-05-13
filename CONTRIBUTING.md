# Contributing to Envoy

Thanks for considering a contribution. Envoy is a Windows-only WPF desktop app that automates job applications using local-first AI. The most valuable contributions are site templates, parser robustness improvements, and adaptive-parser feedback — but bug reports, docs fixes, and small UX polish are equally welcome.

## Code of conduct

By participating, you agree to abide by our [Code of Conduct](CODE_OF_CONDUCT.md).

## Build and run

**Prerequisites:**
- Windows 10/11 (64-bit)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Ollama](https://ollama.com/download) (optional — only required if you want to test against a local model)
- Google Chrome or Microsoft Edge

**Clone, restore, build, test:**
```powershell
git clone https://github.com/LXBStudioLLC/envoy
cd envoy
dotnet restore
dotnet build -c Release
dotnet test
```

**Run the desktop app:**
```powershell
dotnet run --project src/Envoy.UI
```

The first launch will create `%LOCALAPPDATA%\Envoy\` for settings, SQLite database, and crash logs.

## Repo layout

```
src/
  Envoy.Core/         core services, data layer, LLM providers, browser/CDP
  Envoy.UI/           WPF views, themes, app host (entry point)
  Envoy.Assets/       PDF generation, fonts
  Envoy.Templates/    JSON templates for supported job boards
tests/
  Envoy.Core.Tests/   xUnit + Moq tests for Core services
docs/
  ADAPTIVE_PARSER.md       adaptive parser scoring, fingerprints, relocation
  TEMPLATE_AUTHORING.md    walk-through for adding new job-board templates
  SETUP.md                 user-facing install / first-launch
  internal/                internal design notes (not user-facing)
```

## Where to start

- **Add a job-board template** — highest-leverage community contribution. See [docs/TEMPLATE_AUTHORING.md](docs/TEMPLATE_AUTHORING.md).
- **Fix a parser regression** — adaptive parser logs DOM drift events to `%LOCALAPPDATA%\Envoy\relocations.jsonl`. PR-driven updates to existing templates that close those drift events are very welcome.
- **Improve safety guardrails** — see `src/Envoy.Core/Services/SafetyService.cs` for the multi-layer validation pipeline.
- **Docs** — anywhere the README, SETUP, or in-app strings disagree with current behavior.

## Branch & commit conventions

- Branches: `feature/<short-name>`, `fix/<short-name>`, `docs/<short-name>`, `template/<board-name>`.
- Commits: imperative present tense, no Conventional Commits prefix required. Examples:
  - `Add Ashby template`
  - `Fix DOM drift on Greenhouse multi-step apply`
  - `Document DPAPI scope caveat in SECURITY.md`

## Pull requests

Open a PR against `main`. The PR template will walk you through the summary, type, test plan, and AGPL agreement checkbox. CI runs `dotnet restore` / `build` / `test` on Windows for every PR — green CI is a prerequisite for review.

If your change adds a new job-board template, include:
- The board name and a representative job URL you tested against (do not include personal info in the URL).
- A screenshot of the form before/after the fill.
- A note in `CHANGELOG.md` under the unreleased section.

## Style notes

- **`ConfigureAwait(false)` is intentionally omitted.** Envoy.Core is consumed
  only by Envoy.UI in-process; nothing else takes a dependency on it. The WPF
  consumer wants its continuations on the UI thread for status updates. Don't
  blanket-add `ConfigureAwait(false)` to existing awaits — it would force
  marshaling work back to the UI thread anyway and obscures the call sites.
  If you ever split Envoy.Core into a separately consumed library (e.g. a
  service worker), revisit this choice.
- **File-scoped namespaces** everywhere. Matches the existing pattern and the
  rule in `.editorconfig`.
- **Static frozen brushes** live in `src/Envoy.UI/Theme.cs`. Don't redeclare
  `Color.FromRgb(...)` literals per view — pull from `Theme` via
  `using static Envoy.UI.Theme;`.

## License

Envoy is licensed under [AGPLv3](LICENSE). By submitting a PR you agree your contribution will be released under the same license. Network use without source disclosure is not permitted under AGPL — if you fork Envoy into a hosted service, you must publish your modifications.
