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

---

## Live run 2026-06-11

**Verdict: PASS (live), with one environment defect filed (Service Bus emulator — not a code defect).**
Docker enabled this run (engine 29.5.2 / Compose v5.1.4). Every pipeline hop exercised against live
infrastructure and the **live** Functions API + frontend — not fixtures. The full data path
(USGS feed → parse/serialize → builder → Azurite blob → SQL → API → frontend) ran end-to-end and
produced 3 real story cards. The Service Bus transport hop is broken **in this environment** by an
emulator-image defect (AMQP gateway does not serve); it was substituted with a faithful direct builder
invocation, so no hop went unproven.

### Environment
- Compose stack up, all healthy: `quake-azurite`, `quake-mssql`, `quake-sb-sql` healthy;
  `quake-servicebus` running, "Emulator Service is Successfully Up", `quake-events` queue created
  (matches `ServiceBusOutput`/`ServiceBusTrigger`).
- `.env` from `.env.example`; `local.settings.json` from example (emulator connection string matches).
- EF: `dotnet ef database update --connection "Server=localhost,1433;..."` → migration `20260610105304_Initial`
  applied. (Design-time factory uses a placeholder conn, so `--connection` is required to target the
  container.) **B5 verified live in SQL:** `StoryCards` table + `IX_StoryCards_QuakeId` **unique** index
  (is_unique=1) present — dedup is real.
- Host: `func start --cors "*" --port 7071` — all 4 functions indexed; **no** ServiceBusConnection
  parse error this run (emulator conn string is valid). `GET /api/cards` → HTTP 200 `[]` pre-run.

### Hop-by-hop
| Hop | Status | Evidence |
|---|---|---|
| 1. USGS poll (live feed→parse) | **PASS (live)** | Timer + manual admin-invoke: `USGS poll: 19 quakes in feed, 19 new` against live `4.5_day.geojson`. |
| 2. SB publish (poller output) | **FAIL — environment** | `ServiceBusException: Connection refused (ConnectionRefused)`. Root-caused below. **Not a code defect.** |
| 3. SB message on queue | **N/A** (blocked by hop 2 env defect) | Substituted: real poller-shaped messages fed directly to the builder. |
| 4. Builder (deserialize→assemble→blob→SQL→log) | **PASS (live)** | Drove the **real** builder via `POST /admin/functions/StoryBuilderFunction` with 3 messages produced by the **real** `UsgsFeedParser` + `QuakeJson.Options` from the live feed. All 3 `Succeeded`. Exact gate log emitted: `Story card created for M5.5 128 km NW of Vallenar, Chile -> 2026/06/us7000ss82.json` (+ 2 more). Live `INSERT INTO [StoryCards]` observed; 3 rows in SQL. |
| 5. API + frontend (live) | **PASS (live)** | `GET /api/cards` → 3 cards, camelCase keys `{quakeId,magnitude,place,city,country,occurredUtc}` (B2 live). `GET /api/cards/us7000ss82` → full StoryCard with `location:null, wiki:null, photos:[]` but live `weather` (Open-Meteo) + `history` (USGS) (B3 blob round-trip live). Frontend served + driven by Playwright against the **live** API: **14/14** checks (list grid, badges, detail, **degraded card**), 0 console/page errors. Screenshots: `frontend/.verify/live-list.png`, `live-detail.png`, `live-detail-degraded.png`. |

### B1 (the headline boundary) — proven live
The builder deserialized the live, camelCase, `QuakeJson.Options`-serialized poller payloads with **no
error and full fidelity** (M5.5 / place / coords / occurredUtc all preserved into SQL). Serializer
agreement holds on real feed data, not just round-trip fixtures.

### Service Bus emulator defect (environment — does NOT block the gate or implicate project code)
- Symptom: SDK `SendMessageAsync` → `ConnectionRefused`. TCP to `localhost:5672` connects; HTTP health
  (`:5300/health`) returns `{"status":"healthy"}`; logs say "Successfully Up"; `quake-events` created.
- Root cause isolated: an AMQP-protocol probe (`printf 'AMQP\x00\x01\x00\x00'` to `5672`) gets **zero
  bytes back, then close** — the AMQP gateway accepts TCP but does not serve AMQP. Reproduced **from a
  peer container inside the compose network** (`servicebus-emulator:5672`), ruling out host/WSL port
  forwarding. Reproduced across image tags **`:latest` (Jan-2026), `1.1.2`, and `1.0.1`** and after a
  clean recreate of the emulator + its backing SQL. Independent of project code (minimal SDK probe fails
  identically to the Functions host).
- Conclusion: defect in the Service Bus emulator image on this WSL2 kernel, not in `docker-compose.yml`,
  `config.json`, the connection strings, or the functions. Filed as a defect task for infra-engineer.

---

## Live SB transit verification 2026-06-12

**Verdict: FAIL — the SB transit hop does NOT work end-to-end through the Functions host.**
**Root cause is NOT the prior "startup race" diagnosis; it is a Service Bus client SDK version
incompatibility with the emulator.** The emulator itself is healthy and serves real AMQP traffic
(proven below with a working SDK roundtrip), but the version of `Azure.Messaging.ServiceBus`
bundled inside the Functions host's in-proc Service Bus extension (**7.17.1**) cannot connect to
the emulator and fails every send/receive with `ConnectionRefused`. This affects BOTH the poller's
`[ServiceBusOutput]` and the builder's `[ServiceBusTrigger]`.

### Environment (this run)
- `docker compose up -d --wait` → returned after **60s**; all services healthy including
  `quake-sb-ready` (the new AMQP-handshake sidecar). `docker exec quake-sb-ready` AMQP header probe
  → **8 bytes** echoed back (gateway serving). Emulator logs: "Emulator Service is Successfully Up",
  queue `quake-events` created.
- EF migration `20260610105304_Initial` already applied (persisted `mssql-data` volume);
  `IX_StoryCards_QuakeId` unique index present. **0 cards pre-run** (clean slate).
- Functions host: `func start --cors "*" --port 7071` — all 4 functions indexed
  (`UsgsPollerFunction: timerTrigger`, `StoryBuilderFunction: serviceBusTrigger`, 2 HTTP). No
  ServiceBusConnection parse error. `GET /api/cards` → HTTP 200 `[]` pre-run.

### The transit attempt (genuine poller path) — FAILED
Triggered the real poller via `POST /admin/functions/UsgsPollerFunction` (HTTP 202). Live feed
fetched fine, then the SB output binding failed:
```
[10:14:33] Received HTTP response headers after 909ms - 200   (live USGS feed)
[10:14:33] USGS poll: 14 quakes in feed, 14 new
[10:14:45] Executed 'Functions.UsgsPollerFunction' (Failed, Duration=12426ms)
[10:14:45] Exception while executing function: Functions.UsgsPollerFunction. Connection refused
           ErrorCode: ConnectionRefused (ServiceCommunicationProblem).
```
Reproduced identically on a second manual invoke and on the 5-min timer fire — **the poll/parse
succeed, only the SB publish fails**, ~12s each (SDK retry exhaustion). The exact symptom the prior
run filed — but the prior root cause (race) is now disproven.

### Root cause isolated — SDK version, not readiness, not emulator
A standalone `Azure.Messaging.ServiceBus` console app against the **exact same** connection string
(`…UseDevelopmentEmulator=true;`) and the **same running emulator**, run at the same time:
| SDK version | Result |
|---|---|
| **7.20.1** (infra's proof version) | `SEND OK` → `RECEIVE OK` → `ROUNDTRIP VERIFIED` |
| **7.17.2** (worker-side bundled) | `SEND FAILED: ServiceBusException: Connection refused (ConnectionRefused)` |

Same code, same string, same emulator, same minute — only the SDK version differs. 7.17.x refuses;
7.20.1 works. The Functions host transport DLLs (`bin/output/.azurefunctions/`):
- `Azure.Messaging.ServiceBus` asm **7.17.1.0** (file 7.1700.123) — the in-host AMQP transport
- `Microsoft.Azure.WebJobs.Extensions.ServiceBus` asm **5.13.5.0**
- (worker-side) `Microsoft.Azure.Functions.Worker.Extensions.ServiceBus` **5.16.0** → pulls
  `Azure.Messaging.ServiceBus` **7.17.2**

All pre-7.18 — the emulator's `UseDevelopmentEmulator=true` AMQP negotiation needs a newer SDK.

### Why `quake-sb-ready` gives a false-positive
The sidecar healthcheck sends the 8-byte AMQP **protocol header** and passes when bytes echo back.
That proves the gateway is past its SQL bootstrap, but it does **not** prove a full SASL+AMQP
session can be opened — which is exactly what a real SDK client (and the Functions host) needs.
So `--wait` returns "healthy" while the host's bundled SDK still can't actually connect. The
readiness gate closes the race window but does not address the SDK incompatibility, and its green
state is misleading for this purpose.

### Builder consume side — also broken (same SDK)
Sent a correctly-shaped, poller-equivalent `QuakeEvent` message into `quake-events` via the working
**7.20.1** client (`SEND OK`), then watched the running host: the `StoryBuilderFunction`
ServiceBusTrigger **never fired**. A non-destructive `PeekMessages` showed the message sitting
**unconsumed** in the queue (`seq=2, body={"id":"qa7000transit01",…}`). The trigger listener uses
the same in-host 7.17.1 SDK and cannot connect, so it drains nothing. (Test message drained
afterward; queue left clean, 0 cards, API `[]`.)

### Pass criterion result
The task's pass criterion — "the message went THROUGH the emulator: poller send succeeds over AMQP
and the ServiceBusTrigger delivers it" — is **NOT met**. Neither the genuine poller path nor the
SDK-injected-message-into-running-builder equivalent works, because both the output binding and the
trigger run on the incompatible in-host SDK. The transport substitution from the prior run therefore
**cannot yet be retired**: the residual stays OPEN.

### This is a code/dependency defect (owner: backend-engineer), not environment
Unlike the prior run's finding, this is fixable in the repo:
- **Fix:** raise the Service Bus extension so the in-host `Azure.Messaging.ServiceBus` is ≥ ~7.18
  (the emulator-compatible line; 7.20.1 is proven working here). Bump
  `Microsoft.Azure.Functions.Worker.Extensions.ServiceBus` (currently 5.16.0) to a version whose
  WebJobs extension carries a ≥7.18 transport, and re-run this transit proof.
- Re-verify: `docker compose up -d --wait`; `func start`; trigger poller → expect `Story card
  created…` from a ServiceBusTrigger execution (not a `/admin/...StoryBuilderFunction` invoke);
  `curl /api/cards` non-empty.

### Stack state
Left **RUNNING** (all healthy) per task instruction. Functions host left running on :7071.
Standalone SDK probe project at `/tmp/sbprobe` (throwaway).

---

### Re-run after SDK fix 2026-06-12 — **PASS (live, genuine poller path, zero substitution)**

**The defect is fixed and the SB transit hop now works end-to-end through the real emulator.**
backend-engineer bumped `Microsoft.Azure.Functions.Worker.Extensions.ServiceBus` 5.16.0 → **5.24.0**
in `src/Quake.Functions/Quake.Functions.csproj` (no code changes; attribute APIs unchanged).

**SDK version confirmed loaded before judging (the prerequisite check):** killed the old host
(it had the 7.17.1 DLLs locked in `bin/output`), `dotnet publish -c Debug -o bin/output`, restarted
`func start --cors "*"`. The in-host transport DLLs are now:
- `bin/output/.azurefunctions/Azure.Messaging.ServiceBus.dll` → asm **7.20.1.0** (was 7.17.1.0)
- `bin/output/.azurefunctions/Microsoft.Azure.WebJobs.Extensions.ServiceBus.dll` → asm **5.17.0.0** (was 5.13.5.0)

(Note: `func start --no-build` from the project dir misfired — "No job functions found", wrong
`.azurefunctions` artifact root. Plain `func start` with the implicit build indexed all 4 functions
correctly. Recorded for the README/next runner.)

**Pre-run:** all compose services healthy; `GET /api/cards` → `[]`; SQL `StoryCards` = 0.

**Genuine poller path — every hop live, no substitution:**
Triggered via `POST /admin/functions/UsgsPollerFunction` (HTTP 202).

Send side (poller `[ServiceBusOutput]` — the hop that failed before):
```
[12:13:07.286] USGS poll: 13 quakes in feed, 13 new
[12:13:07.634] Executed 'Functions.UsgsPollerFunction' (Succeeded, Duration=1097ms)
```
**No `ConnectionRefused`.** Succeeded in ~1s (the broken runs took ~12s to fail on retry exhaustion).

Transit + consume side (builder `[ServiceBusTrigger]` — the broker actually delivered the messages):
```
[12:13:07.458] Executing 'Functions.StoryBuilderFunction' (Reason='(null)', Id=…)
[12:13:07.461] Trigger Details: MessageId: 34ac0052…, SequenceNumber: 3, DeliveryCount: 1, EnqueuedTimeUtc: 12:13:07.367…
   … 13 trigger executions total, SequenceNumber 3–15, DeliveryCount: 1 each …
[12:13:10.413] Story card created for M5.5 128 km NW of Vallenar, Chile -> 2026/06/us7000ss82.json
[12:13:10.414] Executed 'Functions.StoryBuilderFunction' (Succeeded, Duration=2826ms)
   … 13 "Story card created" lines, all 13 StoryBuilderFunction executions Succeeded …
```
The `Trigger Details` with broker-assigned `SequenceNumber`/`EnqueuedTimeUtc`/`DeliveryCount` are the
definitive proof the messages **transited the emulator queue** and were delivered by the ServiceBus
trigger — not a direct `/admin` builder invoke. SequenceNumbers begin at 3 (1–2 were prior probe
messages), confirming the same queue. (`Reason='(null)'` is normal for a ServiceBusTrigger delivery.)

**Sink:** `GET /api/cards` → **13 cards**, camelCase keys `{quakeId,magnitude,place,city,country,occurredUtc}`;
SQL `StoryCards` = **13**. Post-run `PeekMessages` on `quake-events` → **0 remaining** (builder
consumed every message; no stuck/unconsumed message, unlike the failed run where one sat at seq=2).

**Pass criterion met:** poller SEND succeeds over AMQP **and** the ServiceBusTrigger delivers
(13/13). The transport substitution from the live run is retired. The SB-substitution residual is
**CLOSED**.

**Stack state:** left RUNNING (all healthy); Functions host running on :7071 with the 7.20.1 SDK.
