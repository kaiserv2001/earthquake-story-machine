# 08 — Map Check (Sprint 3, Lane B)

**Owner:** frontend-engineer
**Task:** B.1–B.3 — Map view of quake story cards
**Status:** Built and verified against contract-shaped fixtures via the real `app.js` fetch path. Live-API verification is QA's authoritative pass (Q.2) — the Functions host on :7071 was down (HTTP 000) during this work.

## What was built

A new **map view** added to the existing single-page app (still no framework, no build step). Only `frontend/app.js` and `frontend/styles.css` changed; `index.html` and `staticwebapp.config.json` were untouched (hash routes are client-side; the CDN loads need no CSP/route change because no CSP is defined).

| File | Change |
|---|---|
| `frontend/app.js` | New `#/map` route + `renderMap()`; `buildViewToggle()` segmented control; Leaflet CDN lazy-loader; coordinate resolver with bounded concurrency; null-safe marker plotting; `buildEmptyState()` refactor (shared by list + map). |
| `frontend/styles.css` | `.view-toggle`, `.map`, `.map-status`, `.map-popup*`, and Leaflet control/popup dark-theme overrides. |

### B.1 — Map view
- **Route:** `#/map`, driven by the existing hash router (added one branch).
- **Entry point:** a **List ⇄ Map segmented toggle** rendered at the top of both the list view and the map view (plain anchors → router drives navigation). The toggle also appears on the list's empty state so the view is always reachable.
- **Markers:** `L.circleMarker` (vector, no marker-image asset dependency), colored by the **same magnitude tier scale** as the badges (major red / strong orange / moderate amber / minor blue), radius scaling gently with magnitude. Each marker's popup shows the magnitude badge, relative time, place, location line, and a **"View story card →" link to `#/card/{quakeId}`** — the existing detail route.
- **Framing:** `fitBounds` over all plotted markers (single marker → fixed zoom 5).
- **Theme:** CARTO `dark_all` raster tiles + Leaflet controls/attribution/popups restyled to the seismic tokens (`--surface`, `--border`, `--accent`). Consistent with the existing dark UI.

### Library choice — Leaflet 1.9.4 via unpkg CDN + CARTO dark tiles
- **Why Leaflet:** the only mature, dependency-free mapping lib that drops in via a `<script>`/`<link>` tag with zero build step — fits the vanilla/no-bundler constraint exactly. Loaded **lazily** (only when the map view is first opened) so the list view pays nothing.
- **SRI pinned:** both the CSS and JS CDN tags carry the official 1.9.4 `integrity` SHA-256 hashes (verified against leafletjs.com/download.html) + `crossorigin`.
- **Why CARTO dark_matter tiles (not standard OSM):** standard OSM tiles are light and clash with the seismic dark theme; CARTO `dark_all` is free and on-theme. **Attribution for both OpenStreetMap and CARTO is rendered** by Leaflet's attribution control (verified present), satisfying both providers' terms.

### B.2 — Null-safety / coordinate sourcing (load-bearing)
- **Contract gap, handled without inventing a field:** `GET /api/cards` (the list) carries **no** lat/lon per the FINAL contract — only `GET /api/cards/{id}` has `quake.latitude`/`quake.longitude`. Rather than request a backend change mid-sprint, the map **reads coordinates from each card's detail endpoint**, fetched with **bounded concurrency** (`MAP_CONCURRENCY = 6`).
- **Per-card failures are swallowed, never fatal:** a detail fetch that 404s or rejects resolves to `null` and the card is simply excluded — one dead card cannot blank the map.
- **`hasFiniteCoords()` guard:** a card is plotted only if lat/lon are finite numbers within valid ranges. `null`/`NaN`/out-of-range coords → excluded. (The contract types these as numbers, but the guard defends against nulls regardless.)
- **Transparency:** a status line reports `Showing N quakes · M without coordinates not shown`.
- **Empty data state:** when `GET /api/cards` returns `[]`, the map shows the **same empty state** as the list (`buildEmptyState`, shared helper) with map-appropriate copy, toggle still present. When cards exist but none have plottable coordinates, the map container hosts the empty state instead of a blank canvas.
- **Fetch failure:** `GET /api/cards` failure (or Leaflet CDN failure) → error banner with a "Try again" button, consistent with the list's failure state. Never a blank page.

## How it was verified

**Method:** fixture-based, exercising the **real** `app.js` fetch code path (`USE_MOCK` left **off**). A throwaway harness (`frontend/.verify/map-harness.html`) stubs `window.fetch` for `/api/*` only, so `app.js`'s actual `fetch('/api/cards')` and per-card `fetch('/api/cards/{id}')` calls run; the Leaflet CDN passes through to the network.

**Why not the live host:** the Functions host on :7071 returned HTTP 000 (down/restarting — backend was concurrently editing src/) throughout this task. Fixture-against-real-fetch-path is the documented fallback; QA owns the authoritative live check (Q.2).

**Fixture shape (4 cards) deliberately mixes:** one major-tier card with good coords, one moderate-tier card with good coords, one card whose detail returns **null** lat/lon, and one card whose detail **404s** — so exclusion + null-safety are both exercised.

**Harness:** `frontend/.verify/map-check.mjs` (Playwright + bundled Chromium, headless). `.verify/` is gitignored (root `.gitignore` line 9) — dev-only, not shipped.

**Result: 12/12 checks passed.**
- 2 of 4 markers plotted (null-coord card and 404 card excluded — no crash).
- Status line: "Showing 2 quakes · 2 without coordinates not shown".
- Map toggle active on `#/map`; toggling to List shows the grid; toggle present on both views.
- Attribution control present and names **OpenStreetMap** and **CARTO**.
- Marker popup link targets `#/card/{quakeId}`.
- Empty data (`[]`) → empty state shown, toggle present, **no** Leaflet canvas mounted.
- Zero uncaught console/page errors.

Screenshots: `frontend/.verify/map.png` (two tier-colored markers on dark tiles), `map-popup.png`, `map-empty.png`.

## What QA (Q.2) should specifically re-check against the LIVE host
1. **Live render:** open `#/map` against the 13 real cards on :7071; confirm markers plot at the real epicenters with correct tier colors, and the status count = number of live cards with coordinates.
2. **N+1 detail fetch cost:** the map issues one `GET /api/cards/{id}` per listed card (concurrency-capped at 6). With 13 cards this is fine; confirm acceptable on the live host. *If the card count grows large, the clean fix is backend adding lat/lon to the `GET /api/cards` summary — a contract change, not a frontend workaround. Flagging as a follow-up, not a defect.*
3. **Null coords in live data:** if any real card legitimately lacks coordinates, confirm it is excluded and the status line reflects it (live data may not contain this case; the fixture proves the path).
4. **CDN reachability:** Leaflet (unpkg) and CARTO tiles are third-party CDNs; confirm they load in the test environment. SRI hashes are pinned — a CDN content change would (correctly) block the script; the map then shows its error banner rather than a broken page.
5. **Zero console errors** on the live map view.

## Notes / follow-ups
- **No contract field was invented.** The map works strictly within the FINAL contract by reading coordinates from the detail endpoint. The only *optional* improvement would be backend adding `latitude`/`longitude` to the list summary to eliminate the per-card detail fetches — noted for backend-engineer's consideration, not required for this deliverable.
- `index.html` and `staticwebapp.config.json` unchanged. Touched only `frontend/` (app.js, styles.css) and this check file, per assignment scope. Changes left uncommitted.
