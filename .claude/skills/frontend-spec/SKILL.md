---
name: frontend-spec
description: Design tokens, layout spec, and API contract rules for the Earthquake Story Machine frontend. Read before writing or modifying anything in frontend/ — index.html, app.js, styles.css — and when changing how story cards are listed or rendered.
---

# Frontend Spec — Story Card Browser

## Stack constraints
Vanilla HTML + ES modules + CSS. No framework, no bundler, no npm. Azure Static Web Apps serves the folder as-is; `/api/*` proxies to the Functions app (local: `func start` on :7071 — use a `const API_BASE` switched on `location.hostname === 'localhost'`).

## API contract
The contract is `_workspace/api-contract.md`, owned by backend-engineer. Until it exists, the plan defines:
- `GET /api/cards` → `[{ quakeId, magnitude, place, city, country, occurredUtc }]` (newest first, ≤50)
- `GET /api/cards/{quakeId}` → full `StoryCard` JSON (camelCase): `quake{...}`, `location?`, `wiki?`, `weather?`, `photos[]`, `history?`, `generatedUtc`

Every `?` section can be null — **each card section renders independently and disappears gracefully when its data is null.** A card with only quake facts must still look complete.

## Design tokens (seismic dark theme)
```css
:root {
  --bg: #0d1117;            /* page */
  --surface: #161b22;       /* cards */
  --border: #21262d;
  --text: #e6edf3;
  --text-dim: #8b949e;
  --accent: #ff6b35;        /* seismic orange */
  --mag-moderate: #d29922;  /* M 4.5–5.4 */
  --mag-strong:  #f0883e;   /* M 5.5–6.4 */
  --mag-major:   #f85149;   /* M 6.5+   */
  --radius: 12px;
  --font: system-ui, -apple-system, "Segoe UI", sans-serif;
}
```
Magnitude badge: filled pill, `M 6.1` in white on the tier color; tier function lives in `app.js` and is the only place the thresholds appear.

## Layout
- **List view:** responsive grid `repeat(auto-fill, minmax(320px, 1fr))`; each tile = magnitude badge, place, city/country line, relative time ("3 h ago").
- **Detail view:** same page, hash-routed (`#/card/{quakeId}`); order: hero photo (first of `photos`, full-bleed top, attribution bottom-right overlay) → title (place + badge) → fact strip (depth, time UTC+local, coords) → weather chip → wiki extract with "Read more" link → history strip ("N quakes within 300 km in 30 days · strongest last year M x.x") → photo thumbnails.
- **Empty state:** centered message "No story cards yet — the machine is listening for earthquakes 🌍" — this is the first thing anyone sees on a fresh deploy; make it intentional.
- **Failure state:** fetch error banner with retry button; never a blank page.

## Non-negotiables
- Unsplash attribution: photographer name linking to `photographerUrl` on every rendered photo (API terms).
- All dynamic text through `textContent`/attribute assignment — never concatenate API data into `innerHTML` (wiki extracts and place names are third-party strings).
- Dates: ISO from the API; render with `Intl.DateTimeFormat`/relative-time helper, never string-slice.
