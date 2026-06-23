# Ghost Detection Fixtures

This directory contains labeled sample job postings for testing the ghost-detection signal framework.

## Format

Each `.json` file is a serialized `Envoy.GhostDetection.Models.JobPosting`:

- `Id` must be a valid GUID.

```json
{
  "Id": "...",
  "Source": 0,          // 0=Greenhouse, 1=Lever, 2=Indeed, 3=LinkedIn, 4=Workday, 5=Ashby, 6=Other
  "CompanyName": "...",
  "JobTitle": "...",
  "Location": "...",
  "DescriptionText": "...",
  "Url": "...",
  "PostedAtUtc": "2026-01-01T00:00:00Z",
  "LastUpdatedUtc": "2026-01-01T00:00:00Z",
  "SalaryText": "...",
  "RawSourceId": "...",
  "Extra": {}
}
```

## Contributing a fixture

When you add a new signal, include 1–2 fixture files that exercise it:

1. Name the file `posting-<scenario>.json` (kebab-case, descriptive).
2. Include a mix of:
   - **Real postings** that should score low (Neutral band)
   - **Ghost/suspicious postings** that should score high (Elevated or High band)
3. Add a brief comment at the top of the JSON (not valid JSON, but accepted by our test parser as a `//` line) explaining why this posting is labeled the way it is.
4. Reference the fixture in your xUnit test via `JsonSerializer.Deserialize<JobPosting>`.

## Existing fixtures

| File | Scenario | Expected band |
|---|---|---|
| `posting-aggregated-no-ats.json` | Indeed aggregator, no direct ATS URL | Neutral (no ATS to cross-check) |
| `posting-greenhouse-live.json` | Direct Greenhouse URL, job confirmed live | Neutral (confirmed on ATS) |
| `posting-lever-stale.json` | Direct Lever URL, old posting | Elevated (age + not on ATS) |
| `posting-linkedin-inferred.json` | LinkedIn aggregator, company inferred | Neutral (inferred ATS check) |
| `posting-age-stale-junior.json` | Junior role open a long time (PostingAge signal) | Neutral/Elevated by age |
| `posting-dupjd-template-farm.json` | Boilerplate JD reused across postings (DuplicateJd signal — Weak) | Elevated |
| `posting-repost-bumped.json` | Unchanged posting repeatedly relisted (RepostFrequency signal — Weak) | Elevated |
| `posting-scam-telegram-crypto.json` | Off-platform Telegram redirect + crypto payment (ScamPattern signal — Deterministic) | High |

## Labels

If you have real-world postings you'd like to contribute (with the company's permission or from public boards), include:
- The posting JSON
- Your manual label: `real` or `ghost` + a one-sentence reason
- The signal(s) that caught it (if known)

Submit via PR against `main` from a `feature/<name>` branch.
