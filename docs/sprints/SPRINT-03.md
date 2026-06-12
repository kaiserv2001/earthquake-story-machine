# Sprint 3 — Backlog: Feed, Map, Diagram, Portfolio

**Goal:** Ship the four user-approved backlog items: RSS/Atom feed of story cards, map view of quakes, architecture diagram for the README, portfolio entry.
**Plan:** backlog list in `docs/sprints/SPRINT-02.md` Notes (no new master plan — items are additive features on the Sprint 1–2 pipeline).
**Team:** backend-engineer (feed), frontend-engineer (map), qa-engineer (incremental + gate); diagram + portfolio entry = lead (quake-orchestrator). User approved all four items 2026-06-12.
**Prerequisite:** Sprint 2 gate GO; SB-emulator residual CLOSED (live transit verified 2026-06-12); CI green on `main`.
**Exit criteria (gate):** feed serves valid Atom XML from live local data · map renders the live cards with null-safe popups · diagram artifact linked from README · portfolio entry lands in /mnt/d/code/Portfolio · `_workspace/qa/gate_sprint3.md` = GO

## Lane A — Backend: Atom feed (backend-engineer; read `dotnet-conventions` skill first)
| # | Depends on | Deliverable | Status |
|---|---|---|---|
| A.1 | — | HTTP Function `GET /api/feed` serving Atom 1.0 XML of recent story cards (reuse the card repository; correct `Content-Type`, XML escaping, stable entry ids) | ☑ |
| A.2 | A.1 | Contract update: feed endpoint appended to `_workspace/api-contract.md` (backend owns the contract) | ☑ |
| A.3 | A.1 | Unit tests for feed generation (escaping, empty-feed case); build 0 warn / 0 err | ☑ |

## Lane B — Frontend: map view (frontend-engineer; read `frontend-spec` skill first)
| # | Depends on | Deliverable | Status |
|---|---|---|---|
| B.1 | — | Map view of quake story cards (lat/lon from card data; marker → card link; entry point from the list view; consistent with the seismic dark theme) | ☑ |
| B.2 | B.1 | Null-safe behavior: cards missing coordinates degrade gracefully; empty-data state | ☑ |
| B.3 | B.1–B.2 | Check file `_workspace/08_map_check.md` | ☑ |

## Lane C — Lead (quake-orchestrator)
| # | Depends on | Deliverable | Status |
|---|---|---|---|
| C.1 | — | Architecture diagram artifact (dark-themed HTML/SVG, `docs/architecture.html`) linked from README | ☑ |
| C.2 | A + B gated | Portfolio entry via `add-portfolio-project` (links the GitHub repo — no deployed URL yet) | ☑ |

## QA (qa-engineer, incremental)
| # | Trigger | Checks | Status |
|---|---|---|---|
| Q.1 | A.1 done | Feed: well-formed Atom (validate XML), escaping of card text, contract ↔ implementation match, live response on compose stack | ☑ |
| Q.2 | B.1 done | Map: renders live local cards, marker data ↔ API contract fields, null-safe with missing coords, 0 console errors | ☑ |
| Q.3 | all done | `_workspace/qa/gate_sprint3.md` go/no-go | ☑ **GO** |

## Notes
- **GATE = GO (2026-06-12)** — `_workspace/qa/gate_sprint3.md`. **Zero defects.** Both lanes verified live on the compose stack (QA recovered Docker Desktop mid-pass): feed XML-parsed on the wire with `application/atom+xml`, feed ↔ `/api/cards` identical ids/order/cap, escaping proven by live injection; map plotted 14/14 live cards at real epicenters with the contract fields cross-read on both sides, null-coord/404 exclusion proven live, zero console errors. Regression: build 0 warn/0 err, 16/16 tests (9 existing + 7 new `AtomFeedBuilderTests`).
- Feed design: pure `AtomFeedBuilder` in Quake.Core (unit-testable, `XDocument`-based escaping); `StoryFeedFunction` queries the same SQL index as `/api/cards` (`OrderByDescending(OccurredUtc).Take(50)`) so feed and list always agree. Contract updated by backend (owner).
- Map design: Leaflet 1.9.4 via unpkg CDN with pinned SRI, lazy-loaded only when the map opens; CARTO dark tiles (OSM + CARTO attribution); `#/map` route with List ⇄ Map toggle; coordinates come from each card's detail endpoint (list has no lat/lon per contract), concurrency-capped at 6.
- **Known follow-up (backend-engineer, not a defect):** map does N+1 detail fetches for coordinates — fine at current card counts; clean fix is adding lat/lon to the `/api/cards` summary (a contract change) if card volume grows.
- C.1 diagram built by the lead per the Architecture Diagram skill, render-verified in headless Chromium, linked from README. C.2 portfolio entry added to /mnt/d/code/Portfolio `projectData.deployments` (badge WIP, GitHub link — flip to LIVE with a `liveUrl` once deployed to Azure); left uncommitted in that repo.
- Deployment to real Azure remains out of scope.
