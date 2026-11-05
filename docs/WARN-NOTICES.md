# WARN Notices Seed Dataset

`HiringFreezeSignal` (ghost signal, Tier: Probabilistic) cross-checks a posting's
employer against recent public WARN Act layoff notices. WARN requires employers
with 100+ employees to give 60 days notice of mass layoffs; state labor agencies
publish those notices. This is public government data: read-only, no login, no
scraping behind auth.

## Where the data comes from

The bundled seed currently holds **162 real notices from the Washington State
Employment Security Department WARN database**
(<https://fortress.wa.gov/esd/file/WARN/Public/SearchWARN.aspx>), scraped on
**2026-11-05**, covering notice dates **2025-10-17 through 2026-11-04**.

To extend coverage to other states (California EDD, New York DOL WARN, Texas
TWC, …), scrape each agency's public page into the record format below and
append to the file.

## File format

`src/Envoy.GhostDetection/warn-notices.json` — a JSON array of records:

```json
{
  "Employer": "Seattle Marriott Redmond",
  "State": "WA",
  "Location": "Redmond",
  "NoticeDate": "2026-11-04",
  "AffectedCount": 91
}
```

| Field | Notes |
|---|---|
| `Employer` | Employer name as the state agency published it. |
| `State` | Two-letter state code. |
| `Location` | Free-text city/cities as published (optional). |
| `NoticeDate` | Date the agency posted/received the notice, ISO `yyyy-MM-dd`. |
| `AffectedCount` | Workers affected; `0` when not published. |

The runtime load order is: `%LOCALAPPDATA%\Envoy\warn-notices.json` (a future
refresher writes there), then the bundled seed next to the app, then an empty
dataset (the signal simply never fires). Loaded data is cached for the process
lifetime.

## Refresh cadence — future work

The signal only reads the local dataset; it never fetches per posting. The
bundled seed ages out of the signal's 90-day window over time, so a small
refresher that pulls the latest state WARN pages into the cache on some cadence
(weekly, say) is the natural follow-up. Trigger none of it inside
`EvaluateAsync` — that would break the per-posting budget.
