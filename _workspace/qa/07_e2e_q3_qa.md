# QA Pass 07 — Q.3 e2e on compose stack + frontend live render

**Owner:** qa-engineer · **Trigger:** B.1 (docker-compose) done
**Verdict: PARTIAL — live steps DEFERRED (environment), static wiring PASS.**
**Reason for defer:** Docker is unavailable in this WSL distro — `docker` not found, daemon down, `docker compose`
absent (Docker Desktop WSL integration off). Same environment limitation as the Sprint-1 gate. Per orchestrator
error-handling, Docker-dependent steps are marked **deferred, not passed**; everything verifiable without Docker
was verified.

## What the live e2e would prove (the 5 hops) and current status
| Hop | Proof | Status |
|---|---|---|
| 1. Stack up | `docker compose up` → azurite + mssql + servicebus-emulator + servicebus-sql healthy | **DEFERRED** (no Docker) |
| 2. Poller fires | timer → USGS feed → parse → publish to `quake-events` | **DEFERRED** (needs host+SB); wiring PASS |
| 3. SB message | message visible on `quake-events` queue | **DEFERRED**; queue declared in emulator config |
| 4. Builder logs | `"Story card created for M{Mag} {Place} -> {Blob}"` | **DEFERRED**; log line present at StoryBuilderFunction.cs:52 |
| 5. API + frontend | `curl /api/cards` returns the card; frontend renders it live | **DEFERRED**; API + frontend paths PASS static |

## Static verification completed this pass (everything Docker-independent)

### Stack config (docker-compose.yml + emulator config) — PASS
- YAML parses clean: 4 services (`azurite`, `mssql`, `servicebus-sql`, `servicebus-emulator`) + 2 volumes.
- Azurite exposes blob/queue/table (10000–10002) → satisfies `BlobStorageConnection` + `AzureWebJobsStorage`
  (`UseDevelopmentStorage=true`).
- `mssql` on 1433 → `SqlConnection` (`Server=localhost,1433`). Healthcheck via sqlcmd present.
- `servicebus-emulator` AMQP 5672, backed by dedicated `servicebus-sql`; `depends_on: service_healthy`.
- **Queue-name agreement (critical):** emulator `infra/servicebus-emulator/config.json` declares queue
  **`quake-events`** — matches `ServiceBusOutput("quake-events")` (UsgsPollerFunction.cs:19) and
  `ServiceBusTrigger("quake-events")` (StoryBuilderFunction.cs:22). No drift.
- `.env.example` supplies `MSSQL_SA_PASSWORD` (the only required var; compose uses `:?` fail-fast).

### Pipeline wiring (the e2e chain) — PASS, statically coherent end-to-end
- **B1 serializer agreement re-confirmed:** poller serializes each quake with `QuakeJson.Options`
  (UsgsPollerFunction.cs:41); builder deserializes with the **same** `QuakeJson.Options`
  (StoryBuilderFunction.cs:26). The historically highest-risk hop is sound — already proven with negative
  controls in Sprint-1 pass 04.
- Builder: deserialize → dedup (`AnyAsync` on `QuakeId`) → `assembler.AssembleAsync` → `store.SaveAsync` (blob)
  → SQL insert → **logs the exact proof string** `"Story card created for M{Mag} {Place} -> {Blob}"` (line 52).
- API list reads SQL `StoryCards` ordered by `OccurredUtc` desc, Take(50) → the same rows the builder writes.
- **Build green:** `dotnet build -warnaserror` → 0 warnings / 0 errors. `dotnet test` → 9/9. A live run would
  have a valid binary; only the runtime infra is missing.

### Frontend live-render path — PASS static (closes Lane A's open item at static level only)
- `frontend/app.js` `API_BASE` = `http://localhost:7071` when host is localhost/127.0.0.1, else `''`
  (Static Web Apps proxy). `USE_MOCK = false` — the **real** fetch path is the default. Live calls go to
  `/api/cards` and `/api/cards/{id}` (lines 116, 134).
- `staticwebapp.config.json` proxies `/api/*` (anonymous) and rewrites navigation/404 → `/index.html` for the
  hash router. Correct for a linked-backend SWA.
- Lane A's 29/29 DOM checks (pass via Playwright against contract-derived fixtures) + the Q.1 runtime casing
  confirmation mean the **only** thing the deferred live render adds is proving the real API emits the same
  bytes the fixtures used. Q.1 already serialized the real `StoryCard`/list types through the live formatter
  default and got camelCase — so the fixture↔live gap is now very small, but the actual live DOM render
  remains **DEFERRED**.

## Defects
**None found.** No render defect to route to frontend-engineer (live render not exercisable here).

## Close-out procedure (when Docker/real infra is available — for the user or a future run)
1. `cp .env.example .env`; `docker compose up -d`; wait for healthy.
2. Apply EF migrations to QuakeDb (`dotnet ef database update -s src/Quake.Data` against local SQL).
3. `func start` in `src/Quake.Functions` with `local.settings.json` (copy from example).
4. Trigger the poller (wait for `%UsgsPollSchedule%` or invoke manually) → watch for
   `"USGS poll: N quakes…"` then `"Story card created…"`.
5. `curl localhost:7071/api/cards` → expect a non-empty JSON array, camelCase keys.
6. Serve `frontend/` (localhost) → list grid renders the live cards; click one → detail view renders;
   confirm a degraded card (null enrichment section) still renders. This closes Lane A's live-render item.
