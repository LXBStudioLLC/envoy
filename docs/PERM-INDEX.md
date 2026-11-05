# PERM Filing Cross-Check Index

`PermCrossCheckSignal` (ghost signal, Tier: Probabilistic) cross-checks a
posting against recent **PERM labor-certification filings** from the public
U.S. DOL/ETA Office of Foreign Labor Certification disclosure data. Under PERM,
an employer sponsoring a foreign worker for a green card must publicly
advertise the role to test the labor market, so a live posting matching a
recent filing may be a legally required ad for a role that is already
effectively filled. Correlational, not proof. Public government data: read-only,
no login, no scraping behind auth.

## The size problem, and the fix

The raw OFLC PERM disclosure files are several tens to hundreds of MB of Excel
per year. Envoy **never ships, downloads, or parses them per-posting**. Instead,
a bundled, pre-shrunk index (`src/Envoy.GhostDetection/perm-index.json`,
~0.14 MB / ~1,600 rows) is generated out-of-band and keyed for in-memory lookup
at load time.

## How the bundled seed was generated (2026-11-05)

1. Download the **FY2026 Q3 disclosure file** —
   <https://www.dol.gov/media/PERM_Disclosure_Data_FY2026_Q3.xlsx> (156 MB).
   This file is cumulative over received cases and covers every row this
   release needs; the FAQ-quarter files for the same fiscal year are subsets.
2. Filter to filings with `RECEIVED_DATE` ≥ **2025-11-05** (the last 12 months
   at generation time). Older rows are dropped.
3. Keep only the columns the match needs: employer name (`EMP_BUSINESS_NAME`,
   falling back to `EMP_TRADE_NAME`), job title (`JOB_TITLE`, falling back to
   `PWD_SOC_TITLE`), work state (`PRIMARY_WORKSITE_STATE`), SOC code
   (`PWD_SOC_CODE`), and the filing date.
4. De-duplicate on (normalized employer, lowercase title, state), keeping the
   most recent filing date per combination.

## File format

```json
{
  "GeneratedAt": "2026-11-05",
  "Source": "U.S. DOL ETA OFLC PERM Disclosure Data, FY2026 Q3 (…)",
  "ReceivedFrom": "2025-11-05",
  "ColumnOrder": ["Employer", "JobTitle", "State", "SocCode", "ReceivedDate"],
  "Rows": [
    ["ZoomInfo Technologies, LLC", "Senior Director, Applications", "WA", "11-3021.00", "2026-02-25"]
  ]
}
```

`Rows` are compact positional arrays (order per `ColumnOrder`) to keep the file
small; normalization happens at load time in C#.

## Runtime load order

1. `%LOCALAPPDATA%\Envoy\perm-index.json` — user-refreshed index (see below);
2. the bundled seed next to the app;
3. an empty list (the signal then never fires — graceful, never throws).

Loaded once and cached for the process lifetime. No network access in
evaluation, ever.

## Refreshing the index (shipped: `PermIndexBuilder`)

DOL publishes decisions on a quarterly-ish cadence, and because PERM cases are
typically decided 8–12 months after filing, the "received in the last 12
months" slice of any snapshot ages exactly as fast as the calendar.
`PermIndexBuilder` (`src/Envoy.GhostDetection/Services/PermIndexBuilder.cs`)
regenerates the index on demand:

- **Candidates, newest first**: `PERM_Disclosure_Data_FY…_Q….xlsx` for the
  current OFLC fiscal-year quarter and earlier, then the previous fiscal year
  (the first URL that downloads and passes the sanity gate wins; FY2026 Q3 —
  the file this seed was cut from — is always in the list).
- **Download**: streamed straight to a temp file (never buffered whole in
  RAM), then deleted after parsing either way. dol.gov 403s generic crawler
  user agents, so the request sends the plain `curl/8.0.1` UA that worked when
  the seed was cut.
- **Parse**: a minimal streaming XLSX reader (`ZipArchive` + `XmlReader`, no
  Excel package dependency) handles shared strings, inline strings, and Excel
  serial dates. Verified against the real FY2026 Q3 file: 149 MB parsed in
  ~9 s with flat memory.
- **Shrink**: filter to `RECEIVED_DATE` within the last 12 months; dedupe on
  (normalized employer, lowercase title, state) keeping the most recent
  filing date; sanity gate — fewer than 500 kept rows **rejects** the build
  rather than writing a bad index.
- **Write**: the exact envelope format above (positional rows), atomically, to
  `%LOCALAPPDATA%\Envoy\perm-index.json`, then the in-memory index is swapped
  so the next posting scores with fresh data. The bundled seed is never touched.

In the app: Scoreboard → **PUBLIC DATA → REFRESH PERM INDEX**. A cadence that
follows the DOL quarterly release cycle keeps the 12-month window meaningful.
