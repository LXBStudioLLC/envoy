# Contributing to Envoy

Thanks for considering a contribution. Envoy is a Windows-only WPF desktop app whose **centerpiece is ghost-job detection**: scoring how likely a job posting is a waste of an applicant's time, with transparent evidence. The existing resume-tailoring + form-fill flow remains as a **human-gated copilot**.

## The #1 way to contribute: author a ghost signal (hand it to your agent)

Ghost detection is built on a **signal framework** (`src/Envoy.GhostDetection/`). Each signal is an independent `IGhostSignal` implementation that evaluates a `JobPosting` and returns a `SignalResult` (or `null` for no opinion). Signals run in parallel and are aggregated by `GhostScorer` into a risk band (Neutral / Elevated / High) with human-readable evidence.

The framework auto-discovers every `IGhostSignal` implementation at runtime — **zero wiring, zero DI registration**. Network signals needing `HttpClient` are auto-registered too. This means adding a signal is literally "drop one file in `Signals/`, done."

### Step-by-step

1. Pick an open [`signal:` issue](https://github.com/LXBStudioLLC/envoy/issues?q=is%3Aissue+label%3Asignal) (or propose a new one).
2. Open [`SIGNAL_AUTHORING.md`](SIGNAL_AUTHORING.md) and **copy the agent prompt verbatim**.
3. Paste the prompt into your coding agent (Claude Code, Kimi, Copilot, etc.).
4. Paste the **SPEC** block from the issue into the prompt.
5. Review the generated diff. The agent will create:
   - `src/Envoy.GhostDetection/Signals/<Name>Signal.cs`
   - `tests/Envoy.GhostDetection.Tests/<Name>SignalTests.cs`
   - `tests/Envoy.GhostDetection.Tests/fixtures/<name>-*.json` (if needed)
6. `dotnet test` must pass, including `ContainerResolutionTests`.
7. Open a PR against `main` from a `feature/<name>` branch.

**The runbook already bakes in the precision rules**: default to `null`, earn your confidence, never punish visa sponsorship, human-readable evidence, zero network calls in tests. See [`SIGNAL_AUTHORING.md`](SIGNAL_AUTHORING.md) for the full prompt, interface contract, and definition of done.

See [AGENTS.md](AGENTS.md) for full architecture and constraints.

## Other valuable contributions

- **Fix a parser regression** — adaptive parser logs DOM drift events to `%LOCALAPPDATA%\Envoy\relocations.jsonl`. PR-driven updates to existing templates that close those drift events are very welcome.
- **Improve safety guardrails** — see `src/Envoy.Core/Services/SafetyService.cs` for the multi-layer validation pipeline.
- **Add a job-board template** — see [docs/TEMPLATE_AUTHORING.md](docs/TEMPLATE_AUTHORING.md).
- **Docs** — anywhere the README, SETUP, or in-app strings disagree with current behavior.

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
  Envoy.GhostDetection/  ghost-job detection framework (centerpiece)
  Envoy.Discovery/    sanctioned job discovery (public ATS board APIs + Brave Search)
  Envoy.UI/           WPF views, themes, app host (entry point)
  Envoy.Assets/       PDF generation, fonts
  Envoy.Templates/    JSON templates for supported job boards
tests/
  Envoy.Core.Tests/   xUnit + Moq tests for Core services
  Envoy.GhostDetection.Tests/  xUnit + Moq tests for ghost signals
    fixtures/         labeled sample job postings (tests/Envoy.GhostDetection.Tests/fixtures/)
  Envoy.Discovery.Tests/  xUnit + Moq tests for job discovery
docs/
  ADAPTIVE_PARSER.md       adaptive parser scoring, fingerprints, relocation
  TEMPLATE_AUTHORING.md    walk-through for adding new job-board templates
  SETUP.md                 user-facing install / first-launch
  internal/                internal design notes (not user-facing)
```

## Branch & commit conventions

- Branches: `feature/<short-name>`, `fix/<short-name>`, `docs/<short-name>`, `template/<board-name>`, `signal/<name>`.
- Commits: imperative present tense, no Conventional Commits prefix required. Examples:
  - `Add ATS cross-check signal`
  - `Fix DOM drift on Greenhouse multi-step apply`
  - `Document DPAPI scope caveat in SECURITY.md`

## Pull requests

Open a PR against `main` from a `feature/<name>` branch. The PR template will walk you through the summary, type, test plan, and AGPL agreement checkbox. CI runs `dotnet restore` / `build` / `test` on Windows for every PR — green CI is a prerequisite for review.

**What to expect:** PRs are reviewed by [@LXBStudioLLC](https://github.com/LXBStudioLLC). We aim to give a first response within a few days — if it's been longer, a polite bump on the PR is welcome. **Draft PRs are encouraged** for work in progress or early feedback. If you want to sanity-check an approach *before* writing code, open (or comment on) the relevant [`signal:` issue](https://github.com/LXBStudioLLC/envoy/issues?q=is%3Aissue+label%3Asignal) first — that's cheaper than a rewrite.

If your change adds a new ghost signal, include:
- The signal name and intended data source
- Fixture JSON files with labeled sample postings
- A note in `CHANGELOG.md` under the unreleased section

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
