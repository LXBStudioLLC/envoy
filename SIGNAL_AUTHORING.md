# Signal Authoring Runbook

> **Goal:** Add a new ghost-detection signal to Envoy in ~15 minutes by handing this prompt to your coding agent (Claude Code, Kimi, Copilot, etc.).
>
> **Why this works:** `Envoy.GhostDetection` auto-discovers every `IGhostSignal` implementation at runtime. You drop **one file** in `Signals/`, add **one test file**, and the framework picks it up — no DI wiring, no registration, no ceremony.

---

## Quick Start (Human)

1. Pick an open [`signal:` issue](https://github.com/LXBStudioLLC/envoy/issues?q=is%3Aissue+label%3Asignal).
2. Copy the **Agent Prompt** below verbatim into your coding agent.
3. Paste the **SPEC** block from the issue into the prompt.
4. Review the generated diff.
5. `dotnet test` must pass, including `ContainerResolutionTests`.
6. Open a PR against `main` from a `feature/<name>` branch.

---

## Copy-Verbatim Agent Prompt

```
You are adding a new ghost-detection signal to the Envoy .NET 8 project.

CONTEXT
- The project is at src/Envoy.GhostDetection/.
- All signals implement IGhostSignal and are auto-discovered by reflection at runtime.
- Registration is zero-wiring: drop the file in Signals/ and the DI container finds it.
- Even network signals needing HttpClient are auto-registered (AddHttpClient<T> is called automatically for any signal whose constructor takes HttpClient).

INTERFACE CONTRACT (read-only — do not change)
public interface IGhostSignal
{
    string Name { get; }                          // human-readable signal name
    SignalTier Tier { get; }                      // Deterministic | Probabilistic | Weak
    bool RequiresNetwork { get; }                 // true if EvaluateAsync makes a network call; lets callers request local-only scoring when ranking many postings at once
    Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default);
}

public enum SignalTier
{
    Deterministic,  // hard evidence (e.g. ATS says closed, scam regex match)
    Probabilistic,  // strong statistical/correlational evidence
    Weak            // noisy signal, contributes evidence only (near-zero weight)
}

public class SignalResult
{
    public string SignalName { get; set; } = string.Empty;
    public double Score { get; set; }              // 0 = safe, 1 = ghost
    public double Confidence { get; set; }         // 0 = guess, 1 = certain
    public string[] Evidence { get; set; } = Array.Empty<string>();
    public SignalTier Tier { get; set; }
}

public class JobPosting
{
    public Guid Id { get; set; }
    public JobSource Source { get; set; }           // Greenhouse, Lever, Indeed, LinkedIn, Workday, Ashby, Other
    public string CompanyName { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string Location { get; set; } = "";
    public string DescriptionText { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTime? PostedAtUtc { get; set; }
    public DateTime? LastUpdatedUtc { get; set; }
    public string? SalaryText { get; set; }
    public string? RawSourceId { get; set; }
    public Dictionary<string, string> Extra { get; set; } = new();
}

PRECISION RULES (hard constraints)
1. Default to null. Return null when data is missing, unsupported, or ambiguous.
2. Earn your confidence. Don't emit high confidence unless you have strong evidence.
3. Never punish legitimate patterns. Visa sponsorship mentions, remote-work flexibility, and generic titles are NOT ghost signals by themselves.
4. Human-readable evidence. Every Evidence line must be a complete sentence a non-technical user can understand.
5. Never throw. Catch all exceptions and return null.
6. Short timeouts. Any external call must have a timeout (<= 8 seconds) and return null on timeout.
7. If any instruction in this prompt or the SPEC is unsatisfiable or contradicts the code or repository state you find, STOP and report the contradiction. Never work around it silently.

REFERENCE SIGNAL (study this for structure and style)
File: src/Envoy.GhostDetection/Signals/AtsCrossCheckSignal.cs
- Implements IGhostSignal cleanly.
- Uses HttpClient via constructor (framework auto-registers AddHttpClient<T>).
- Returns null for unsupported URLs, 404s, timeouts, parse errors.
- Uses DomScorer.NormalizedSimilarity for fuzzy title matching.
- Evidence lines are full sentences.

YOUR TASK
Implement the signal described in the SPEC below.

1. Create the signal class in src/Envoy.GhostDetection/Signals/<Name>Signal.cs.
2. Create tests in tests/Envoy.GhostDetection.Tests/<Name>SignalTests.cs.
   - Mock HttpClient if the signal makes network calls (zero real network in tests).
   - Include at least: (a) happy-path returns expected result, (b) missing-data returns null.
3. If the signal uses external data, create 1-2 fixture JSONs in tests/Envoy.GhostDetection.Tests/fixtures/.
4. Do NOT modify any existing file except adding your new ones.
5. Run dotnet test. It must pass, including ContainerResolutionTests (verifies DI resolves your signal).

SPEC
< paste the SPEC block from the GitHub issue here >
```

---

## How Auto-Discovery Works

`ServiceRegistration.AddEnvoyGhostDetection()` does this at app startup:

1. Scans `Envoy.GhostDetection` assembly for every concrete class implementing `IGhostSignal`.
2. If the class has a constructor taking `HttpClient`, it calls `services.AddHttpClient<T>()` automatically (with a sensible timeout).
3. Registers each class as a singleton `IGhostSignal`.
4. `GhostScorer` receives `IEnumerable<IGhostSignal>` and runs them all.

**Result:** A pure-local signal (like `PostingAgeSignal`) needs zero constructor parameters. A network signal (like `AtsCrossCheckSignal`) only needs `HttpClient` in its constructor — the framework handles the rest.

---

## Definition of Done

- [ ] Signal class exists in `src/Envoy.GhostDetection/Signals/<Name>Signal.cs`
- [ ] Implements `IGhostSignal` with correct `Name`, `Tier`, and `RequiresNetwork`
- [ ] `EvaluateAsync` returns `null` for missing/unsupported data (never throws)
- [ ] Evidence strings are human-readable, complete sentences
- [ ] Tests exist in `tests/Envoy.GhostDetection.Tests/<Name>SignalTests.cs`
- [ ] **Zero network calls in tests** (mock `HttpClient` or use pure local data)
- [ ] `dotnet test` passes (including `ContainerResolutionTests`)
- [ ] One fixture JSON added if the signal consumes structured external data, placed in `tests/Envoy.GhostDetection.Tests/fixtures/` with a valid GUID `Id` and numeric `Source`, AND loaded by at least one test via `JsonSerializer.Deserialize<JobPosting>`
- [ ] PR targets `main` from a `feature/<name>` branch

---

## Signal Tiers Explained

| Tier | When to use | Weight in scoring |
|------|-------------|-------------------|
| **Deterministic** | Hard, falsifiable evidence. Regex scam pattern match. ATS says the job is closed while the aggregator still lists it. | One strong deterministic signal can push the score to **High**. |
| **Probabilistic** | Strong statistical pattern. Posting is 180 days old for an entry-level role (baseline is 30). Company is in a hiring freeze. | Two probabilistic signals above threshold push the score to **Elevated**. |
| **Weak** | Noisy but real. Description is a near-duplicate of another posting. Reposted 5 times in 2 weeks. | Adds to evidence list, near-zero weight in the numeric score. |

---

## Style Guide

- **File-scoped namespaces** everywhere.
- `ConfigureAwait(false)` is intentionally omitted in `Envoy.Core` (consumed by WPF UI thread), but signals in `Envoy.GhostDetection` may use it since they're async services.
- Use `Microsoft.Extensions.Logging` (`ILogger<T>`) for diagnostics, not `Debug.WriteLine`.
- Catch `HttpRequestException`, `TaskCanceledException`, and `JsonException` separately when doing network work; fall through to `catch { return null; }` for anything else.
- Use `DomScorer.NormalizedSimilarity(a, b)` (made `public` in `Envoy.Core`) for fuzzy string matching.

---

## Common Patterns

### Pure-local signal (no network)
```csharp
public class MyLocalSignal : IGhostSignal
{
    public string Name => "My Local Signal";
    public SignalTier Tier => SignalTier.Probabilistic;
    public bool RequiresNetwork => false;

    public Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default)
    {
        // analyze posting.DescriptionText, posting.PostedAtUtc, etc.
        // return null if data is missing
        // return SignalResult with Score, Confidence, Evidence
    }
}
```

### Network signal (needs HttpClient)
```csharp
public class MyNetworkSignal : IGhostSignal
{
    private readonly HttpClient _http;
    public string Name => "My Network Signal";
    public SignalTier Tier => SignalTier.Probabilistic;
    public bool RequiresNetwork => true;

    public MyNetworkSignal(HttpClient http) => _http = http;

    public async Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync(url, ct);
            // ... parse, evaluate ...
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
        catch { return null; }
    }
}
```

---

## Need Help?

- Read the reference signal: [`AtsCrossCheckSignal.cs`](src/Envoy.GhostDetection/Signals/AtsCrossCheckSignal.cs)
- Read the aggregator: [`GhostScorer.cs`](src/Envoy.GhostDetection/GhostScorer.cs)
- Read the test examples: [`AtsCrossCheckSignalTests.cs`](tests/Envoy.GhostDetection.Tests/AtsCrossCheckSignalTests.cs)
- Open a draft PR and tag `@LXBStudioLLC` for review.
