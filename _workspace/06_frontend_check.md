# 06 — Frontend Check (Sprint 2, Lane A)

**Owner:** frontend-engineer
**Task:** T17 — Story card browser UI (Sprint 2, Lane A, items A.1–A.4)
**Status:** Built and verified against fixture. Live-API verification pending B.1 local stack.

## What was built

A single-page vanilla app (no framework, no build step) served as-is by Azure Static Web Apps.

| File | Purpose |
|---|---|
| `frontend/index.html` | Shell: header, `#app` mount, footer; loads `app.js` as an ES module. |
| `frontend/app.js` | Router + list view + detail view + all state views. Single source of magnitude thresholds. |
| `frontend/styles.css` | Seismic dark theme using the exact design tokens from `frontend-spec`. |
| `frontend/staticwebapp.config.json` | SPA navigation fallback + `/api/*` anonymous route. |
| `frontend/mock-cards.json` | Offline fixture matching `_workspace/api-contract.md` (dev/verify only; not required at runtime). |

### A.1 — List view
- Responsive grid `repeat(auto-fill, minmax(320px, 1fr))`.
- Each tile: magnitude badge (color-tiered), place, city/country line, relative time.
- City/country both nullable → falls back to "Location unavailable" (e.g. ocean epicenters).
- Magnitude tiers live ONLY in `magnitudeTier()` in `app.js`: 4.5–5.4 moderate (amber), 5.5–6.4 strong (orange), 6.5+ red.
- Relative time via `Intl.RelativeTimeFormat` (never string-sliced).

### A.2 — Detail view
- Hash-routed: `#/card/{quakeId}`; back-link returns to `#/`.
- Order per spec: hero photo (full-bleed, attribution overlay bottom-right) → title + badge → fact strip (depth, UTC time, local time, coords, USGS link) → weather chip → wiki extract + "Read more" → history strip → photo thumbnails.
- **Every enrichment section is null-safe and built by its own helper** (`buildWeather`, `buildWiki`, `buildHistory`, `buildThumbnails`). Each returns `null` when its data is absent and is simply not appended. A card with only `quake` facts still renders complete (placeholder hero band + title + fact strip).
- `photos` checked via `.length` (never null per contract); empty → placeholder hero, no thumbnails.
- `history.maxMagnitudeLastYear` independently null-checked (count shows, max omitted when null).
- `quake.url` null → USGS link omitted.
- **Unsplash attribution** rendered on the hero photo AND every thumbnail: photographer name linking to `photographerUrl` (`rel="noopener noreferrer"`).

### A.3 — Empty + failure states
- **Empty** (`GET /api/cards` → `[]`): centered "No story cards yet" with the 🌍 listening message. This is the first thing seen on a fresh deploy.
- **Failure** (network error or non-200): error banner with a "Try again" button that re-runs the fetch. Never a blank page.
- **404 on detail**: dedicated not-found state with a back-link (distinct from the list error banner).
- `staticwebapp.config.json`: `navigationFallback` to `/index.html` (excluding `/api/*` and static assets), `/api/*` anonymous route, 404 → SPA rewrite.

## Security / contract compliance
- All dynamic API text assigned via `textContent` / element properties / `setAttribute` — never concatenated into `innerHTML`. Wiki extracts, place names, and photographer names are third-party strings and are never parsed as HTML.
- All property reads are camelCase, matching the FINAL contract (`QuakeJson.Options`, Web defaults).
- `API_BASE` switches to `http://localhost:7071` off localhost hostnames; in production the empty base lets the SWA linked backend proxy `/api/*`.
- `USE_MOCK` flag in `app.js` defaults to **off**. When `true`, both endpoints read `mock-cards.json` for frameworkless offline dev. Production code path is the default.

## How it was verified

**Method:** fixture-based, with request interception driving the REAL fetch code path (`USE_MOCK` left off; Playwright intercepts `/api/*` and returns fixture JSON, so `app.js`'s actual `fetch('/api/cards')` calls are exercised, not the mock branch).

**Why not the live host:** `GET /api/cards` reads SQL metadata and `GET /api/cards/{id}` reads blob storage. The Functions host needs the full local stack (azurite + mssql + Service Bus emulator) which is infra-engineer's B.1, still in progress at the time of this check. Verifying against a contract-shaped fixture is the documented fallback; live verification is queued (see "Known gaps").

**Harness:** `frontend/.verify/render-check.mjs` (throwaway; not shipped). Serves `frontend/` over a local HTTP server, drives headless Chromium, asserts DOM across every view, and captures screenshots.

**Result: 29/29 checks passed.** Covered:
- List: 3 tiles, correct badge text + tier class for M5.1/M6.8/M4.6, ocean fallback location, non-empty relative time.
- Detail (fully enriched): hero img, Unsplash attribution link, weather chip, wiki section, history strip, thumbnails, 4 facts.
- Null-safe (every section null + no photos + null url): no weather/wiki/history/thumbnails, placeholder hero, fact strip still present, no USGS link.
- Partial (history with null maxMagnitude): count shown, max omitted.
- 404 detail: not-found state + back-link.
- Empty list: empty state shown.
- Error: error banner + retry button, and retry recovers to the list.
- Zero uncaught console/page errors.

Screenshots captured in `frontend/.verify/` (`list.png`, `detail-full.png`, `detail-degraded.png`, `empty.png`, `error.png`).

## Known gaps / follow-ups
1. **Live-API verification pending B.1.** Once the docker-compose stack is up and the Functions host serves `/api/cards`, I will re-run against the real endpoints and update this file. The fixture is contract-derived, so drift risk is low, but live confirmation is the bar.
2. **Real Unsplash images not rendered offline.** Hero/thumbnail `<img>` boxes are empty in the fixture screenshots because the fixture's Unsplash URLs aren't fetched in the harness; layout, attribution, and null-safety are all confirmed. Real images will load against the live API.
3. `.verify/` is a dev-only folder (harness + screenshots). It can be gitignored or removed before deploy — it is not part of the served app.
