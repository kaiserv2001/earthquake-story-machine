# Infra Validation — Sprint 2 Lane B

**Owner:** infra-engineer
**Date:** 2026-06-11
**Scope:** Tasks T18–T20 (Bicep, docker-compose, CI/CD). All validation is **offline** — no
deployment to real Azure (out of sprint scope).

## Deliverables

| File | Task | Purpose |
|---|---|---|
| `docker-compose.yml` | B.1 / T19 | Local stack: Azurite + SQL Server + Service Bus emulator (+ its SQL companion) |
| `infra/servicebus-emulator/config.json` | B.1 | Declares the `quake-events` queue for the emulator |
| `.env.example` | B.1 | Dev-only `MSSQL_SA_PASSWORD` template (`.env` gitignored) |
| `infra/main.bicep` | B.2 / T18 | All eight Azure resources at cost-floor SKUs |
| `infra/main.bicepparam` | B.2 | Params; secrets via `readEnvironmentVariable` (GitHub secrets) |
| `.github/workflows/ci.yml` | B.3 / T20 | Build + test on PR/push, pinned .NET 8 SDK |
| `.github/workflows/deploy.yml` | B.4 / T20 | Provision + deploy on main, secrets via GitHub secrets, gated by `DEPLOY_ENABLED` |
| `README.md` | B.5 / T19 | Clone → run quick-start |
| `src/Quake.Functions/local.settings.example.json` | B.5 | Copy-to-`local.settings.json` template (real file gitignored) |

## Environment

```
bicep   : Bicep CLI version 0.44.1 (installed standalone — az CLI not present)
dotnet  : 10.0.108  (projects target net8.0; CI pins 8.0.x via setup-dotnet)
docker  : engine 29.5.2 / Compose v5.1.4 — UP as of 2026-06-11 (was down at first authoring; see the Service Bus emulator AMQP fix section below for the live run)
az      : not installed
actionlint : 1.7.7 (downloaded standalone)
```

## Validation transcript

### 1. `bicep build infra/main.bicep` — PASS (clean)

```
$ bicep build infra/main.bicep
rc=0          # no errors, no warnings
```

A first build raised `BCP332`: storage account name `stquakestory${uniqueString}` could
reach 25 chars (limit 24). **Fixed** by trimming to `take(uniqueSuffix, 11)` →
`stquakestory` (12) + 11 = 23 chars worst case. Rebuild is clean.

Compiled ARM asserted to contain every config key the Functions host reads (boundary B4)
+ `dotnet-isolated`. The full dependency set is **eight** keys, not six — derived by
grepping the source for both `IConfiguration` reads and trigger binding expressions:

| Key | How the code consumes it | Source |
|---|---|---|
| `AzureWebJobsStorage` | host runtime (queues/state) | platform |
| `FUNCTIONS_WORKER_RUNTIME` | host runtime | platform |
| `ServiceBusConnection` | `Connection = "ServiceBusConnection"` binding expr | UsgsPollerFunction.cs:19, StoryBuilderFunction.cs:22 |
| `UsgsPollSchedule` | `"%UsgsPollSchedule%"` timer binding expr | UsgsPollerFunction.cs:21 |
| `UsgsMinMagnitude` | `IConfiguration["UsgsMinMagnitude"]` | UsgsPollerFunction.cs:24 |
| `UnsplashAccessKey` | `IConfiguration["UnsplashAccessKey"]` | Program.cs:36 |
| `BlobStorageConnection` | `IConfiguration["BlobStorageConnection"]` | Program.cs:45 |
| `SqlConnection` | `IConfiguration["SqlConnection"]` | Program.cs:48 |

All eight are present in `infra/main.bicep` app settings (the two platform keys + the six
app keys), names exact, and match `local.settings.json` / `local.settings.example.json`
1:1. The earlier "six keys" comment in the Bicep (which omitted the two binding-expression
keys from its count, though both were already wired) was corrected to avoid that ambiguity;
QA cross-checks this boundary in Q.2.

### 2. `bicep build-params infra/main.bicepparam` — PASS

```
$ UNSPLASH_ACCESS_KEY=x SQL_ADMIN_PASSWORD=y bicep build-params infra/main.bicepparam
rc=0
```

### 3. `docker compose config -q` — BLOCKED (engine unavailable)

```
$ docker compose config -q
The command 'docker' could not be found in this WSL 2 distro.
We recommend to activate the WSL integration in Docker Desktop settings.
rc=1
```

The `docker` binary is on PATH (Docker Desktop on the Windows host) but WSL integration is
disabled, so even `docker compose config` (which normally needs no running engine) cannot
execute. **Same limitation as Sprint 1.** Fell back to structural validation (below).

### 4. compose structural parse (PyYAML) — PASS

```
$ python3 -c "import yaml; ..."
services: ['azurite', 'mssql', 'servicebus-sql', 'servicebus-emulator']
```

Also verified: emulator `config.json` declares namespace `sbemulatorns` with queue
`quake-events`; `.env.example` defines `MSSQL_SA_PASSWORD`.

### 5. workflows YAML parse (PyYAML) — PASS

```
.github/workflows/ci.yml     -> ['build-test']
.github/workflows/deploy.yml -> ['provision', 'deploy-functions', 'deploy-frontend']
```

Per-job checks: every job has `runs-on` + `steps`; every step has `uses` or `run`. Secret
scan: no hardcoded secret values — credentials only via `${{ secrets.* }}`.

### 5b. `actionlint` — PASS (clean)

```
$ actionlint .github/workflows/ci.yml .github/workflows/deploy.yml
rc=0          # no findings
```

actionlint 1.7.7 (downloaded standalone) validates Actions schema, expression syntax,
`runs-on` labels, and shellcheck on `run:` scripts. Both workflows pass with zero findings.

### 5c. deploy.yml path / param cross-check — PASS

`deploy.yml` references, all confirmed to exist and match:
- `infra/main.bicep` (compiles, §1) with params `unsplashAccessKey` + `sqlAdminPassword` —
  names match the template's `@secure()` params exactly.
- `EarthquakeStoryMachine.sln` and `src/Quake.Functions/Quake.Functions.csproj` (publish step).
- `frontend/` app_location (`Azure/static-web-apps-deploy`) — `frontend/index.html` +
  `staticwebapp.config.json` present.
- The five required secrets the workflow reads match the secrets list documented in
  `README.md`. `functionAppName` flows from the Bicep `output` → `provision` job output →
  `deploy-functions`; that output exists in the template.

### 6. `dotnet restore EarthquakeStoryMachine.sln` — PASS

```
All projects are up-to-date for restore.
rc=0
```

Confirms the solution graph the CI workflow restores/builds/tests is valid. Full
`dotnet build`/`dotnet test` under a pinned net8 SDK is what `ci.yml` runs on the GitHub
runner (this host has only the net10 SDK, so the pinned-SDK build is exercised in CI, not
locally).

### 7. README quick-start (B.5) — verified against the repo

Every command/claim in `README.md` was checked against the actual tree:
- `cp .env.example .env` → `.env.example` exists, defines `MSSQL_SA_PASSWORD`.
- `cp src/Quake.Functions/local.settings.example.json …/local.settings.json` → example exists,
  8 keys, matches the real (gitignored) `local.settings.json`.
- `dotnet ef database update -p src/Quake.Data -s src/Quake.Functions` → migration
  `20260610105304_Initial` exists under `src/Quake.Data/Migrations/`.
- `curl http://localhost:7071/api/cards` → `app.js` fetches `/api/cards` (and `/api/cards/{id}`).
- Deploy secrets list in the README matches `deploy.yml`'s `secrets.*` reads.

**Two accuracy fixes applied during verification:**
- Step 5 now uses `func start --cors "*"`. Locally `app.js` sets `API_BASE` to
  `http://localhost:7071` (cross-origin from any static server), so CORS must be enabled on the
  host — the previous text implied a static-server proxy that doesn't exist in local dev.
- Step 7 clarifies that the `/api/*` proxy applies only to the *deployed* Static Web App;
  locally the frontend calls the Functions host directly via `API_BASE`.

## Carry-forwards from the Sprint 1 gate — addressed

- **B4 config keys** (eight — see §1 table) mirror `local.settings.json` /
  `local.settings.example.json` / the code's `IConfiguration` reads and binding expressions
  exactly in `infra/main.bicep` app settings. Confirmed in compiled ARM.
- **RollForward must not leak into Bicep** — it does not. Function App pins
  `netFrameworkVersion: 'v8.0'`; Azure supplies the net8 runtime. CI pins `8.0.x` via
  `actions/setup-dotnet` rather than relying on roll-forward.

## Cost-floor SKUs (idle ≈ $0)

Service Bus **Basic** · Storage **Standard_LRS** · Functions **Y1** consumption · SQL
**GP_S_Gen5_1** serverless, 60-min auto-pause, min 0.5 vCore · Static Web App **Free** ·
App Insights workspace-based (Log Analytics PerGB2018, 30-day retention).

## Gaps / limitations (honest)

1. **Docker engine unavailable** → `docker compose up` and the e2e run-through (QA Q.3
   against a live stack) cannot be executed in this environment. Compose is validated
   structurally only. To run for real: enable Docker Desktop WSL integration, then
   `docker compose up -d`. Flagged to qa-engineer and team-lead.
2. **`az` / `az bicep` not installed** → used the standalone Bicep CLI (0.44.1) for
   `bicep build`. The spec's `az bicep build --file ...` is equivalent and will pass
   wherever the Azure CLI is present.
3. **No real Azure deployment** — out of sprint scope by design. `deploy.yml` is gated
   behind the `DEPLOY_ENABLED` repo variable and validated offline only.

## Service Bus emulator AMQP fix — root cause + fix (2026-06-11)

**Context:** QA's live run (`_workspace/qa/07_e2e_q3_qa.md`, "Live run 2026-06-11") filed an
environment defect: the emulator accepted TCP on `:5672` but served **zero bytes** to the AMQP
handshake, so every SDK send failed `ConnectionRefused`. QA reproduced it peer-container, across
image tags, and with a minimal SDK probe, and concluded the image was broken on this WSL2 kernel.

**Root cause — a startup race, not a broken image.** With Docker available this run (engine
29.5.2 / Compose v5.1.4) I brought the stack up and read the emulator's *own* logs (the step not
yet taken). Its init runs a multi-step SQL bootstrap — drop + `CREATE DATABASE SbGatewayDatabase`,
`CREATE DATABASE SbMessageContainerDatabase00001`, entity sync, then "Emulator Service is
Successfully Up". The TCP listener on `:5672` opens almost immediately, but the **AMQP gateway does
not serve until that bootstrap finishes**. Measured precisely:

```
TCP port 5672 accepts at +1s
AMQP gateway serves (8 bytes) at +40s
SUMMARY: port_open=+1s  amqp_serves=+40s  gap=39s
```

For ~39s the port accepts connections yet returns zero bytes to the AMQP header probe
(`printf 'AMQP\x00\x01\x00\x00' | nc … 5672`) — **exactly** QA's symptom. QA connected inside that
window; each "clean recreate across tags" simply restarted the same slow bootstrap, so the symptom
reproduced every time and looked tag-independent. Nothing was wrong with `docker-compose.yml`,
`config.json`, the connection strings, or the Functions code. Once the bootstrap completes the
gateway serves normally and echoes the 8-byte AMQP header back.

**Why no in-container healthcheck:** the emulator image is shell-less (CBL-Mariner, no
`sh`/`bash`/`curl`/`wget`/`nc`) and ships no `HEALTHCHECK`, so it cannot probe itself — a known
limitation (Azure/azure-service-bus-emulator-installer issues #35 and #88). A `depends_on:
service_healthy` on the emulator is therefore impossible, and our prior `depends_on` only gated on
the backing SQL container's health, never on the emulator's own AMQP readiness.

**Fix (smallest that works) — a readiness sidecar in `docker-compose.yml`:**
- Added service `servicebus-ready` (`busybox:1.36`, which *does* have `nc`) whose healthcheck sends
  the AMQP 1.0 protocol header to `servicebus-emulator:5672` and is healthy only when bytes come
  back (`… | wc -c | grep -qE '[1-9]'`). An open-but-not-serving port returns zero bytes → unhealthy.
  `start_period: 60s`, `interval: 5s`, `retries: 20`.
- Documented the race at the top of `docker-compose.yml` and switched the README quick-start to
  `docker compose up -d --wait`, which now blocks until `servicebus-ready` is healthy — i.e. until
  the AMQP gateway truly serves. No image wrapping, no fixed sleep.
- No change needed to `config.json` (queue `quake-events` was always declared correctly) or to any
  connection string.

**Proof — real AMQP SDK send + receive (not a port probe):** a standalone `Azure.Messaging.ServiceBus`
7.20.1 console app, using the exact `local.settings.example.json` connection string
(`…;UseDevelopmentEmulator=true;`), sent a poller-shaped message to `quake-events` and received +
completed it:

```
[probe] SEND OK
[probe] RECEIVE OK, body: {"quakeId":"us7000probe","magnitude":5.5,"place":"128 km NW of Vallenar, Chile",…}
[probe] ROUNDTRIP VERIFIED
```

Run after `docker compose up -d --wait` returned (all services healthy, `quake-sb-ready` healthy at
+42s). The emulator works; the transport hop QA had to substitute is now exercisable for real.

**Stack left RUNNING** for QA's task #2 (live SB transit verification). Bring-up command for any
future run: `cp .env.example .env && docker compose up -d --wait`.

## Result

All B.1–B.5 deliverables pass offline validation:
- `bicep build infra/main.bicep` — clean (rc=0, no warnings).
- `bicep build-params infra/main.bicepparam` — clean (rc=0).
- `actionlint` on both workflows — clean (rc=0, zero findings).
- `dotnet restore EarthquakeStoryMachine.sln` — clean (rc=0).
- deploy.yml paths/params/secrets all resolve against the real tree (§5c).
- README quick-start commands verified against the repo; two accuracy fixes applied (§7).
- B4 app-settings boundary: eight config keys, all mirrored in Bicep, names exact (§1).

The only remaining gap is environmental, not a defect in the deliverables: the **Docker
engine is unavailable** in this WSL distro (integration disabled), so `docker compose up` and
the live e2e run-through (QA Q.3) cannot be executed here. Compose is validated structurally
(PyYAML parse + service/queue/secret checks). To run for real, enable Docker Desktop WSL
integration and `docker compose up -d`. Flagged to qa-engineer and team-lead.
