# Sprint 3 Gate — Earthquake Story Machine

**Gate owner:** qa-engineer · **Date:** 2026-06-12
**Recommendation: GO.** Both feature lanes landed and were verified **live** against the running
compose stack + Functions host (not fixtures): the Atom feed serves valid, parseable Atom 1.0 from
live data and agrees with `/api/cards`; the map renders all 14 live cards at real epicenters,
null-safely, with zero app errors. Diagram artifact exists and is linked from the README. Build
0 warn / 0 err, 16/16 tests. Zero defects filed. Portfolio entry is the lead's, pending this gate.

## Environment
- Docker Desktop was **down** at pass start (`/usr/bin/docker` I/O error; daemon pipe absent). Recovered
  by launching Docker Desktop on the Windows host, then `docker compose up -d --wait` → all 5 services
  healthy (`quake-azurite`, `quake-mssql`, `quake-sb-sql`, `quake-servicebus`, `quake-sb-ready`).
- Functions host: plain `func start --cors "*"` (NOT `--no-build`; csproj changed in Sprint 2) → all
  **5** functions indexed incl. the new `GetStoryFeed: [GET] /api/feed`.
- SQL retained the 13 cards from 2026-06-12; the poller fired on host start and added 1 → **14 live cards**
  for the feed/map checks.

## Exit-criteria check (SPRINT-03 line 7)
| Criterion | Status | Evidence |
|---|---|---|
| Feed serves valid Atom XML from live local data | **PASS (live)** | `GET /api/feed` → well-formed Atom 1.0 (XML-parsed, not eyeballed); `Content-Type: application/atom+xml; charset=utf-8`; 14 entries = `/api/cards` exactly (same order, same cap). Q.1 below. |
| Map renders live cards with null-safe popups | **PASS (live)** | 14/14 live cards plotted at real epicenters via Playwright against `:7071`; 13/13 live checks; 404-detail card excluded gracefully with accurate status line. Q.2 below. |
| Diagram artifact linked from README | **PASS** | `docs/architecture.html` (255 lines, valid HTML+SVG, covers USGS→SB→Functions→Blob/SQL→SWA) linked at README.md:21. Browser render verified by the lead. |
| Portfolio entry lands in /mnt/d/code/Portfolio | **PENDING (lead, post-gate)** | C.2 waits for the gate by design (SPRINT-03 Notes). Not a QA item; mark satisfied when the lead runs `add-portfolio-project`. |
| `_workspace/qa/gate_sprint3.md` = GO | **THIS FILE = GO** | below |

## Q.1 — Atom feed (backend-engineer) — PASS (live)
Live `curl http://localhost:7071/api/feed`, validated with a real XML parser (`xml.etree`):
- **Well-formed Atom 1.0**, root `atom:feed`; `Content-Type: application/atom+xml; charset=utf-8`; XML
  declaration `<?xml version="1.0" encoding="utf-8"?>`.
- **Feed-level:** `id` = constant tag URI `tag:earthquake-story-machine,2026:feed`; `title`/`subtitle`/
  `generator` per contract; `link[@rel=self]` echoes the request URL (`http://localhost:7071/api/feed`);
  `updated` is RFC3339-Z and **equals the newest entry's** `updated` (verified, not assumed).
- **Per-entry:** all 14 entries carry `id`/`title`/`updated`/`published`/`summary`; entry `id` =
  `tag:…:quake:{quakeId}`; times RFC3339-Z; `title` = `M{mag} — {location}` with the correct
  `City, Country → Country → place` fallback (checked against each card's fields).
- **Feed ↔ /api/cards agreement (the load-bearing boundary):** feed entry ids == `/api/cards` quakeIds
  **exactly — same order, same set, same 14-cap**. Both sides use `OrderByDescending(OccurredUtc).Take(50)`
  (StoryFeedFunction.cs:22-34 ↔ GetStoryCards). No drift.
- **Escaping — proven live, not just unit-tested:** injected a SQL card with `&`, `<test>`, `"` in
  place/city/country; the live feed still parsed as well-formed XML; on the wire `& → &amp;`,
  `< → &lt;`, `> → &gt;` (no raw `<test>` leaked); the parser decoded back to the literals. Test card
  removed afterward. (Also: `AtomFeedBuilder` uses `XDocument` — escapes structurally, never hand-concat.)
- **Empty-feed validity:** unit test `Empty_feed_is_valid_and_has_no_entries` passes (valid `<feed>`,
  zero entries, not a 404) — matches the contract note.
- **Contract ↔ implementation:** `_workspace/api-contract.md` `## GET /api/feed` matches the live bytes
  field-for-field (title format, id scheme, RFC3339, self link, escaping, empty-feed rule).
- **Tests:** `AtomFeedBuilderTests` 7/7 (escaping, empty, structure); full suite 16/16.

## Q.2 — Map view (frontend-engineer) — PASS (live)
Authoritative live render via Playwright (`frontend/.verify/live-map-check.mjs`): serves `frontend/`
on :8849 so `app.js`'s `API_BASE` resolves to `http://localhost:7071` and the **real** `/api/cards`
+ per-card `/api/cards/{id}` fetches and Leaflet/CARTO CDN loads run. **13/13 checks:**
- **14/14 live cards plotted** at real epicenters (`L.circleMarker`); status line `Showing 14 quakes`;
  **status count == markers == live card count** (cross-checked).
- **Tier colors:** every marker stroke is a tier token (`#d29922` moderate, `#f0883e` strong observed) —
  same scale as the badges.
- **Marker data ↔ contract (boundary, both sides read):** map consumes `card.quake.{latitude,longitude,
  magnitude,place}` from the **detail** endpoint (the list carries no lat/lon per the FINAL contract) +
  `summary.{quakeId,city,country,occurredUtc}` from the **list**. Live detail returns exactly those
  `quake` fields (finite numbers, camelCase); live list returns exactly those summary fields. No mismatch.
- **CDN reachability:** Leaflet JS + CSS from unpkg → **200**; CARTO `dark_all` tiles → **12 requests, all
  200**; attribution control names **OpenStreetMap** and **CARTO**.
- **Popup → card route:** popup "View story card →" link targets `#/card/{quakeId}` (`#/card/us7000ss9q`).
- **Toggle:** List⇄Map segmented control works live (toggle → live grid).
- **Zero console/page errors** on the live map with real data.
- **Null-safe / graceful exclusion — proven live:** injected a SQL card whose detail 404s (no blob); the
  map plotted **14 of 15**, excluded the dead card, and showed `Showing 14 quakes · 1 without coordinates
  not shown` — one dead detail never blanks the map. Test card removed. (The two browser-logged 404 lines
  during this case are network-level logs of the deliberately-swallowed detail fetches in
  `fetchCoordsForCards`'s `try/catch` — **not** uncaught app errors; the clean-data run logged zero.)
- Empty-data state (`[]` → shared `buildEmptyState`, no Leaflet canvas) proven by the frontend fixture
  harness (12/12) + code-read; not re-run live (would require destroying the 14 live cards).
- Screenshots: `frontend/.verify/live-map.png`, `live-map-popup.png`, `live-map-excluded.png`.

## Known follow-up (NOT a defect)
- **N+1 detail fetches on the map.** The list endpoint omits lat/lon (FINAL contract), so the map issues
  one `GET /api/cards/{id}` per card (concurrency-capped at 6). Fine at 14 cards — verified acceptable on
  the live host. If the card count grows large, the clean fix is **backend adding `latitude`/`longitude`
  to the `/api/cards` summary** (a contract change, eliminating per-card fetches). Flagged by
  frontend-engineer; recorded as a follow-up for backend-engineer's consideration, not a Sprint-3 blocker.

## Regression check
- `dotnet build EarthquakeStoryMachine.sln -warnaserror` → **0 warn / 0 err**.
- `dotnet test` → **16/16** passed.
- Existing endpoints intact: `/api/cards` 200 (14), `/api/cards/{id}` 200, feed 200; SB transit hop
  (Sprint-2 fix) still wired — poller fired on host start.

## Open items by severity
**Blocker:** none. **Major:** none.
**Minor / follow-up (do NOT block):**
1. N+1 map detail fetches (above) — follow-up for backend-engineer, fine at current scale.
2. **Uncommitted working tree:** both Sprint-3 lanes (feed: `AtomFeedBuilder.cs`, `StoryFeedItem.cs`,
   `StoryFeedFunction.cs`, tests, contract; map: `app.js`, `styles.css`, `08_map_check.md`),
   `docs/architecture.html` + README link, AND the still-uncommitted Sprint-2 changes (SB extension bump
   5.16.0→5.24.0, `servicebus-ready` sidecar, README `--wait`) are all uncommitted. They should be
   committed together. (Bookkeeping for the lead — not a code defect.)

## Recommendation
**GO for Sprint 3.** Both deliverables are complete and verified against live local infrastructure, not
fixtures: the feed is valid, parseable Atom that agrees with the card list and escapes hostile text on the
real HTTP path; the map renders every live card at its real epicenter, degrades null-safely, loads its CDNs,
and throws no app errors. The diagram exists and is linked. The one open engineering note (map N+1) is a
scale-only follow-up with a known clean fix, not a defect. The portfolio entry is the lead's to land now
that the gate is GO.

## Stack state
Left **RUNNING** per instruction: 5 containers healthy; Functions host on :7071 (5 functions); 14 live
cards; `/api/cards`, `/api/cards/{id}`, `/api/feed` all 200.
