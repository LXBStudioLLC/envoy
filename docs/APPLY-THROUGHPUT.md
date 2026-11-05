# The Applications Escalator — apply-throughput plan

> Adopted 2026-11-05. This plan refocuses Envoy from "ghost-job detector with an
> apply copilot" to **"one profile, one queue, maximum applications filled out
> with the least hassle."** Ghost detection stays: it is the quality rail that
> makes volume *safe* (don't waste an application on a posting that will never
> hire). every work item below is measured against two numbers:
>
> 1. **verified applications per hour** — submissions that actually reached a
>    live company ATS (not a stale aggregator copy),
> 2. **taps per application** — user interactions from "I want this job" to
>    "submitted".
>
> Work items are `APP-01` … `APP-17`. P0 = make the funnel exist, P1 = the
> throughput engine, P2 = volume, safely, P3 = "least hassle" polish.
>
> **Status:** ALL 17 items shipped 2026-11-05 (P0: #72/#76/#74/#73/#75/#77;
> P1: #79/#82/#83/#84; P2: #85/#86/#87/#88/#89; P3: #90/#91), each behind a
> green CI PR. Issues #55–#71 closed. The #92 remainder (first-run checklist
> with per-step actions + auto-hide, tray presence, notifications, Night-Owl
> drain window) and honest taps-per-application telemetry shipped 2026-11-05 —
> nothing shelfed; the Follow-up Items section below describes the current state.

## The ladder (current behavior)

1. **Autofill Copilot (ships now)** — one click anywhere (APPLY on a found
   posting or pasted URL), template fill for 5 boards or the generic LLM
   field mapper for every other ATS, review once, confirm the send.
2. **Gated queue (ships now)** — check results → QUEUE SELECTED → the worker
   tailors everything in the background ("YOUR QUEUE" strip shows the count),
   one-click fills from the panel, human gate on every send; per-company
   throttle + daily cap guard the batch.
3. **Full-Auto (ships now, off by default)** — guarded toggle; SiteClassifier +
   host trust (3 verified submissions or explicit list) + optional UTC window;
   the queue drains prepared items on trusted company/ATS boards only.
   Aggregators and unknowns are structurally excluded.

## Profiles (multi-resume, shipped after the 17)

The queue still reads "one profile" per user intent, but each queue row now
carries the profile it was queued for (`MasterProfileId` stamped at click
time), so multiple resumes coexist honestly:

- **Find Jobs** has an `APPLY WITH` selector; `QUEUE` / `QUEUE SELECTED` and
  the per-row `APPLY` route the chosen profile through (per-row apply carries
  it into the Apply view). One profile per click family; no dialogs.
- **The worker drains rows across profiles** oldest-first — a second resume's
  rows no longer starve behind the first profile's batch. Rows still carry
  their stamp; the single-flight prepare keeps the shared browser safe.
- **Legacy rows** persisted before stamping bind to the first stamped row's
  profile (the old behavior), so upgraded machines keep working.
- **Dashboard** remains the explicit per-profile start (each card's APPLY
  buttons route that profile into the Apply view).

## #92 remainder + taps telemetry (shipped 2026-11-05)

- **First-run checklist** on the Dashboard ends with the "first application"
  step (finished attempts in the ledger); every incomplete row carries one
  action button, and the strip auto-hides when complete.
- **Tray presence**: queue counts in the tooltip, restore-on-click, EXIT menu;
  closing the window minimizes to the tray so a draining queue keeps running.
- **Notifications**: ready-to-fill, captcha hand-off, needs-your-send, failed
  rows, queue finished (sent/failed counts), WARN/PERM refresh completion.
- **Night Owl**: the Full-Auto drain window hours are settable on the Browser
  page; the worker re-consults parked rows the moment the window opens —
  no manual nudge, never a re-prepare. Trust-list entries parse as bare hosts.
- **Taps per application**: TapQueued / TapOpened / TapFilled / TapConfirmed
  journey events (one ledger row per committed action) feed the Scoreboard's
  "taps per application" — average over applications finished in the last 30
  days, joined by posting identity, formula on screen. Batch queueing records
  one row per committed job, so the amortization stays visible.
