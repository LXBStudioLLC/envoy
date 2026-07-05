# Contributing to Envoy

Thanks for thinking about a contribution. Envoy is a Windows-only WPF desktop app built mainly around ghost-job detection: scoring how likely a job posting is a waste of an applicant's time, with transparent evidence. The resume-tailoring and form-fill flow is a human-gated copilot.

## The best way to help: write a ghost signal (and hand it to your agent)

Ghost detection is built on a signal framework (`src/Envoy.GhostDetection/`). Each signal is an `IGhostSignal` that looks at a `JobPosting` and returns a `SignalResult`, or `null` for no opinion. Signals run in parallel, and `GhostScorer` combines them into a risk band (Neutral, Elevated, or High) with human-readable evidence.

The framework finds every `IGhostSignal` at runtime, so there's no wiring and no DI registration. Network signals that need an `HttpClient` are registered automatically too. Adding a signal really is "drop one file in `Signals/`, done."

### Step by step

1. Pick an open [`signal:` issue](https://github.com/LXBStudioLLC/envoy/issues?q=is%3Aissue+label%3Asignal), or propose a new one.
2. Open [SIGNAL_AUTHORING.md](SIGNAL_AUTHORING.md) and copy the agent prompt as-is.
3. Paste the prompt into your coding agent (Claude Code, Kimi, Copilot, and so on).
4. Paste the Spec block from the issue into the prompt.
5. Review the diff. The agent will create:
   - `src/Envoy.GhostDetection/Signals/<Name>Signal.cs`
   - `tests/Envoy.GhostDetection.Tests/<Name>SignalTests.cs`
   - `tests/Envoy.GhostDetection.Tests/fixtures/<name>-*.json` (if needed)
6. `dotnet test` must pass, including `ContainerResolutionTests`.
7. Open a PR against `main` from a `feature/<name>` branch.

The runbook already builds in the precision rules: default to `null`, earn your confidence, never punish visa sponsorship, human-readable evidence, and no network calls in tests. See [SIGNAL_AUTHORING.md](SIGNAL_AUTHORING.md) for the full prompt, the interface contract, and the definition of done.

See [AGENTS.md](AGENTS.md) for the full architecture and constraints.

## Other ways to help

- Fix a parser regression. The adaptive parser logs DOM drift events to `%LOCALAPPDATA%\Envoy\relocations.jsonl`. PRs that update existing templates to close those drift events are very welcome.
- Improve the safety guardrails. See `src/Envoy.Core/Services/SafetyService.cs` for the multi-layer validation pipeline.
- Add a job-board template. See [docs/TEMPLATE_AUTHORING.md](docs/TEMPLATE_AUTHORING.md).
- Docs. Anywhere the README, SETUP, or in-app text disagrees with what the app actually does.

## Build and run

Prerequisites:
- Windows 10/11 (64-bit)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Ollama](https://ollama.com/download) (optional, only needed to test against a local model)
- Google Chrome or Microsoft Edge

Clone, restore, build, test:
```powershell
git clone https://github.com/LXBStudioLLC/envoy
cd envoy
dotnet restore
dotnet build -c Release
dotnet test
```

Run the desktop app:
```powershell
dotnet run --project src/Envoy.UI
```

The first launch creates `%LOCALAPPDATA%\Envoy\` for settings, the SQLite database, and crash logs.

## Repo layout

```
src/
  Envoy.Core/         core services, data layer, LLM providers, browser/CDP
  Envoy.GhostDetection/  ghost-job detection framework (the main feature)
  Envoy.Discovery/    sanctioned job discovery (public ATS board APIs + Brave Search)
  Envoy.UI/           WPF views, themes, app host (entry point)
  Envoy.Assets/       PDF generation, fonts
  Envoy.Templates/    JSON templates for supported job boards
tests/
  Envoy.Core.Tests/   xUnit + Moq tests for Core services
  Envoy.GhostDetection.Tests/  xUnit + Moq tests for ghost signals
    fixtures/         labeled sample job postings
  Envoy.Discovery.Tests/  xUnit + Moq tests for job discovery
docs/
  ADAPTIVE_PARSER.md       adaptive parser scoring, fingerprints, relocation
  TEMPLATE_AUTHORING.md    walk-through for adding new job-board templates
  SETUP.md                 user-facing install and first-launch
  internal/                internal design notes (not user-facing)
```

## Branch and commit conventions

- Branches: `feature/<short-name>`, `fix/<short-name>`, `docs/<short-name>`, `template/<board-name>`, `signal/<name>`.
- Commits: imperative present tense, no Conventional Commits prefix required. For example:
  - `Add ATS cross-check signal`
  - `Fix DOM drift on Greenhouse multi-step apply`
  - `Document DPAPI scope caveat in SECURITY.md`

## Pull requests

Open a PR against `main` from a `feature/<name>` branch. The PR template walks you through the summary, type, test plan, and AGPL agreement checkbox. CI runs `dotnet restore`, `build`, and `test` on Windows for every PR, and green CI is required before review.

What to expect: PRs are reviewed by [@LXBStudioLLC](https://github.com/LXBStudioLLC). We try to give a first response within a few days; if it's been longer, a polite bump on the PR is fine. Draft PRs are welcome for work in progress or early feedback. If you want to sanity-check an approach before writing code, open or comment on the relevant [`signal:` issue](https://github.com/LXBStudioLLC/envoy/issues?q=is%3Aissue+label%3Asignal) first. That's cheaper than a rewrite.

If your change adds a new ghost signal, include:
- The signal name and the data source it uses
- Fixture JSON files with labeled sample postings
- A note in `CHANGELOG.md` under the unreleased section

If your change adds a new job-board template, include:
- The board name and a representative job URL you tested against (no personal info in the URL)
- A screenshot of the form before and after the fill
- A note in `CHANGELOG.md` under the unreleased section

## Style notes

- `ConfigureAwait(false)` is left out on purpose. Envoy.Core is used only by Envoy.UI, in-process, and nothing else depends on it. The WPF side wants its continuations back on the UI thread for status updates. Don't blanket-add `ConfigureAwait(false)` to existing awaits; it would marshal work back to the UI thread anyway and just clutters the call sites. If you ever split Envoy.Core into a separately consumed library (say, a service worker), revisit this.
- File-scoped namespaces everywhere. It matches the existing code and the rule in `.editorconfig`.
- Static frozen brushes live in `src/Envoy.UI/Theme.cs`. Don't redeclare `Color.FromRgb(...)` literals per view. Pull from `Theme` with `using static Envoy.UI.Theme;`.

## License

Envoy is licensed under [AGPLv3](LICENSE). By opening a PR you agree your contribution ships under the same license. AGPL doesn't allow network use without source disclosure, so if you fork Envoy into a hosted service, you have to publish your changes.
