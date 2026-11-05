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
**2026-11-05**, covering notice dates **2025-10-17 through 2026-11-04**. The
refresher (below) extends this live from Washington and New York.

To extend coverage to other states (California EDD, New York DOL WARN, Texas
TWC, …), scrape each agency's public page into the record format below and
append to the file.

## Refreshing the dataset (shipped)

`WarnRefresher` (`src/Envoy.GhostDetection/Services/WarnRefresher.cs`) refreshes
the dataset on demand:

- **Washington** — the WebForms grid at
  <https://fortress.wa.gov/esd/file/WARN/Public/SearchWARN.aspx>: initial GET
  with session cookies, then a POST per page carrying the full WebForms state
  (`__EVENTTARGET=ucPSW$gvMain`, `__EVENTARGUMENT=Page$N`, escaped
  `__VIEWSTATE` / `__EVENTVALIDATION`, empty `__VIEWSTATEENCRYPTED`).
- **New York** — the plain HTML table at <https://dol.ny.gov/warn-notices>.
  NY rows carry a labor-market region, not a city, so `Location` is left null
  (region text could otherwise falsely confirm matches in other states); the
  state code does the matching.

The merge only ever adds: rows the sources no longer list are kept, keyed on
(state, normalized employer, notice date). The result is written atomically to
`%LOCALAPPDATA%\Envoy\warn-notices.json`, and the in-memory dataset is swapped
so the next posting scores with fresh data. The bundled seed is never touched.

A bot-check or CAPTCHA page is surfaced in the result notes and the refresh
stops — it is never bypassed (repo constraint #3). Both sources failing is a
failed refresh; one source failing degrades to a partial refresh with a note.

In the app: Scoreboard → **PUBLIC DATA → REFRESH WARN DATA**.

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

The runtime load order is: `%LOCALAPPDATA%\Envoy\warn-notices.json` (written by
the refresher), then the bundled seed next to the app, then an empty
dataset (the signal simply never fires). Loaded data is cached for the process
lifetime.

## Refresh cadence

The signal only reads the local dataset; it never fetches per posting. A
weekly manual refresh from the Scoreboard keeps the 90-day window meaningful;
nothing refreshes on app start (no surprise network on launch). Extending the
refresher to more states is a matter of adding another source + parser pair.
