# Sprint 2 Gate — Earthquake Story Machine

**Gate owner:** qa-engineer · **Date:** 2026-06-11
**Recommendation: GO** — with two execution-deferred items (live e2e run-through and the CI
runner-green push), both **environment limitations, not code defects**. Zero defects filed across the three
Sprint-2 QA passes (Q.1–Q.3). Build green (0 warn / 0 err, 9/9 tests). Every boundary reachable without
Docker/Azure CLI verified at scripted-or-static; the two deferred items have concrete close-out procedures.

## Exit-criteria check (from SPRINT-02 line 7)
| Criterion | Status | Evidence |
|---|---|---|
| Frontend renders live local pipeline data incl. empty/failure states | **PARTIAL — states MET, live render DEFERRED** | 29/29 DOM checks via Playwright on the **real** fetch path (USE_MOCK off) against contract-derived fixtures — list, detail, all-null degraded card, partial history, 404, empty, error+retry (pass 06_frontend_check). Live-against-real-API render deferred with Q.3 (no Docker). |
| `az bicep build` + `docker compose config` clean | **PARTIAL** | Bicep: **clean** via standalone Bicep CLI 0.44.1 (`az` absent; equivalent) — infra §1. `docker compose config`: **DEFERRED** (Docker engine down); compose validated structurally (PyYAML parse, 4 services + queue-name agreement) — Q.3 + infra §3/§4. |
| CI workflow green on a test push | **PARTIAL — validated, runner-green DEFERRED** | `ci.yml` passes `actionlint` clean; `dotnet restore` of the solution graph succeeds; YAML/job structure valid (infra §5/§5b). No actual GitHub push/runner observed in this environment. |
| `_workspace/qa/gate_sprint2.md` = GO | **THIS FILE = GO** | below |

## Boundary matrix — Sprint-2-relevant rows
| # | Boundary | Status | Level | Where verified |
|---|---|---|---|---|
| B2 | API response ↔ `frontend/app.js` field access | **PASS** | Scripted + Static | Every list/detail field access camelCase, all 4 nullable sections independently null-checked, photos `.length`-guarded. Casing confirmed at **runtime**: real `StoryCard` + anon list projection serialized through `JsonSerializerDefaults.Web` (the live formatter default; no `AddJsonOptions` override) → camelCase, PascalCase negative controls absent (probe 2/2, then removed; suite back to 9/9). Pass 05. |
| B4 | local.settings ↔ Bicep app settings ↔ code reads | **PASS** | Static | All 6 app-specific keys match exact-string across all three sides; the 2 binding-expression keys (`ServiceBusConnection`, `UsgsPollSchedule`) included; 2 platform keys + 4 Azure-only keys correctly placed. Exhaustive reverse-grep: no read-without-setting, no setting-without-read. Independently corroborated by infra §1 (same 8-key table). Pass 06. |
| B1 | poller serialize ↔ builder deserialize | **PASS (carried)** | Scripted | Both use shared `QuakeJson.Options` (UsgsPollerFunction.cs:41 ↔ StoryBuilderFunction.cs:26); proven with negative controls in Sprint-1 pass 04. Re-confirmed in Q.3 wiring read. |
| B3/B5/B6/B7 | blob round-trip / SQL schema / clients / feed | **PASS (carried)** | — | Verified at Sprint-1 gate; unchanged in Sprint 2. |
| e2e | poller→SB→builder log→`/api/cards`→frontend | **DEFERRED** | Static wiring PASS | All 5 hops statically coherent end-to-end; queue name `quake-events` agrees across poller output / builder trigger / emulator config; "Story card created" log present (line 52). Live run blocked on Docker. Pass 07. |

## Sprint-2 deliverable status
- **Lane A (frontend) — COMPLETE.** A.1–A.4 ☑. List grid + badge tiers, hash-routed detail, all enrichment
  sections null-safe, empty/failure/404 states, `staticwebapp.config.json` proxy + SPA fallback. XSS-safe
  (`textContent`/`setAttribute`, never `innerHTML`). 29/29 DOM checks. Open item (live render) → deferred with e2e.
- **Lane B (infra) — COMPLETE on disk.** docker-compose (4 services + SB emulator config declaring
  `quake-events`), `infra/main.bicep` (+ `.bicepparam`, 8 resources at cost-floor SKUs, builds clean),
  `ci.yml` + `deploy.yml` (actionlint clean, `DEPLOY_ENABLED`-gated, secrets via GH secrets), README quick-start,
  `_workspace/07_infra_validation.md`. (Board task #3/B.6 was still marked in_progress at gate-write time, but its
  deliverable is complete and QA-verified — bookkeeping lag, not missing work.)

## Open items by severity
**Blocker:** none.
**Major:** none.
**Minor / deferred (do NOT block the gate):**
1. **Live e2e + live frontend render — execution-deferred.** Docker engine unavailable (WSL integration off);
   `az`/`bicep`/real Azure absent. Same limitation as the Sprint-1 gate. Close-out (also closes Lane A's live
   item): enable Docker Desktop WSL integration → `cp .env.example .env` → `docker compose up -d` → EF migrate →
   `func start --cors "*"` → confirm `"USGS poll…"` + `"Story card created…"` logs → `curl /api/cards` non-empty →
   serve `frontend/` and confirm list+detail+degraded render. Procedure detailed in pass 07.
2. **CI runner-green — execution-deferred.** `ci.yml` is actionlint-clean and the solution restores, but no
   actual GitHub push was made here. Close-out: push a branch / open a PR and confirm the `build-test` job goes
   green on the runner (it pins `8.0.x`; local host only has net10, so the pinned-SDK build is exercised in CI).
3. **`az bicep build` exact command — equivalent-only.** Validated via standalone Bicep CLI 0.44.1 (`bicep build`),
   not `az bicep build`; equivalent output. Will pass wherever Azure CLI is installed.
4. **Informational (no code change) — API contract wording.** `_workspace/api-contract.md` says HTTP responses
   serialize "via `QuakeJson.Options`"; the HTTP layer actually uses ASP.NET Core's MVC formatter. The two
   coincide on casing (both Web defaults) so behavior is correct, but a future `QuakeJson.Options` naming-policy
   change would NOT affect API output. Suggest a one-line contract clarification (for backend-engineer). Pass 05.

## Recommendation
**GO for Sprint 2.** Both lanes are complete and internally coherent. The frontend↔API boundary (B2) — the
sprint's highest-risk seam — is verified in both directions with casing confirmed at runtime, not assumed. The
config boundary (B4) matches exactly across local.settings, Bicep, and code, cross-checked independently by QA
and infra. Bicep compiles clean, both workflows lint clean, the compose stack is structurally sound with the
queue name agreeing across all three places it appears, and the full pipeline is statically wired end-to-end.
The only unmet items are the live end-to-end run, the live frontend render, and the CI runner-green push — all
three gated on infrastructure this environment does not provide (Docker engine, GitHub runner, Azure CLI), all
anticipated by the plan as out-of-environment, and all deferred with concrete close-out steps rather than
failed. No defects were found in any Sprint-2 pass.
