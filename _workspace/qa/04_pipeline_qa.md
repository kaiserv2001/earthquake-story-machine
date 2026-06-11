# QA Pass 04 — Pipeline B1/B3/B4 + e2e (Wave 3.5)

**Task:** #20 · **Owner:** qa-engineer · **Date:** 2026-06-10
**Execution levels (per hop, honest):**
- B1 serializer match — **Scripted** (round-trip through real `Quake.Core.QuakeJson.Options` + negative control)
- B3 blob round-trip — **Scripted** (round-trip through the real blob-store options + negative control)
- B4 config keys — **Static** (cfg[...] reads ↔ local.settings.json diff) + **Live** host-boot confirms binding
- B2 API casing — **Static** (contract ↔ serializer camelCase confirmed; full HTTP response is Sprint 2 frontend scope)
- Host boot / function indexing — **Live** (`func start --port 7073`, all 4 functions indexed)
- Full poller→queue→builder→blob→SQL e2e — **EXECUTION-DEFERRED** (no Docker engine; placeholder
  connections; no Unsplash key) — see "e2e" below. This is the only hop not run end-to-end.

**Verdict: PASS (no defects).** Every code-level boundary verified at scripted-or-better. The single
gap is the live infra-dependent e2e run, which the Sprint 1 plan itself anticipated as Docker-bound.

## Build / tests
`dotnet build -warnaserror` → 0 Warning(s), 0 Error(s). `dotnet test` → 9/9 passed.

## B1 — poller serialize ↔ builder deserialize (THE headline boundary) — PASS (scripted)
Read BOTH sides:
- Poller `UsgsPollerFunction.cs:41`: `JsonSerializer.Serialize(q, QuakeJson.Options)`.
- Builder `StoryBuilderFunction.cs:26`: `JsonSerializer.Deserialize<QuakeEvent>(message, QuakeJson.Options)`.
Same shared `Quake.Core.QuakeJson.Options` instance (Web/camelCase) — matches the lead-approved B1 spec
(SPRINT-01 note line 53), superseding the plan's bare `JsonSerializer` calls.

**Scripted proof** (round-trip a QuakeEvent through the REAL options, compiled against Quake.Core):
- Wire is camelCase: `{"id":...,"magnitude":5.1,...,"depthKm":61.705,"occurredUtc":"...","url":...}`.
- Round-trip restores the record with full value-equality — all 7 required fields + Url preserved.
- **Negative control:** deserializing that camelCase wire with bare-default (PascalCase) options
  **throws JsonException** (required props can't bind). So the shared-options choice is load-bearing,
  not cosmetic — a one-sided "fix" toward defaults would break the pipeline loudly here (and silently
  null fields for non-required shapes). No B1 defect. ✔

## B3 — StoryCard ↔ blob JSON ↔ API GetAsync — PASS (scripted)
- `BlobStoryCardStore` uses `new(QuakeJson.Options){ WriteIndented = true }` — a clone of the shared
  options — for BOTH `Serialize` (SaveAsync) and `Deserialize` (GetAsync). Same options both directions.
- API `GetStoryCard` returns `store.GetAsync(...)` → rides the same options.
**Scripted proof:** a fully-populated StoryCard (all nullable sections set + nested Photos/Location/
Wiki/Weather/History) round-trips through the real blob options with full fidelity; output is indented
and camelCase (`"generatedUtc"`). Negative control: bare-default deserialize of the camelCase blob
**throws**. No B3 defect. ✔

## B4 — config keys: Program.cs/functions cfg[...] ↔ local.settings.json — PASS (static + live boot)
Every key read in code exists in `local.settings.json` Values (exact, case-sensitive):
| Key | Read at | In local.settings.json |
|---|---|---|
| `UnsplashAccessKey` | Program.cs:36 | ✔ (placeholder) |
| `BlobStorageConnection` | Program.cs:45 | ✔ |
| `SqlConnection` | Program.cs:48 | ✔ |
| `UsgsMinMagnitude` | UsgsPollerFunction.cs:24 | ✔ |
| `UsgsPollSchedule` (`%token%`) | UsgsPollerFunction TimerTrigger | ✔ |
| `ServiceBusConnection` | poller `[ServiceBusOutput(Connection=...)]` + builder `[ServiceBusTrigger(Connection=...)]` | ✔ |
- Trigger/output `Connection` attribute names match the settings key (`ServiceBusConnection`). ✔
- `local.settings.json` is **gitignored** (`.gitignore:4`) — placeholder secrets won't be committed. ✔
- Unsplash key registration is null-safe: Program.cs only attaches the `Client-ID` header when the key
  is present and not the `<...>` placeholder, so the host boots without a key (photos degrade to empty).
  Carry to Sprint 2: Bicep app settings must supply these SAME key strings in Azure (B4 cross-check
  deferred to infra — local↔code side is clean now).

## B2 — API response casing ↔ frontend (forward check for Sprint 2) — static, consistent
`_workspace/api-contract.md` is FINAL and declares camelCase (Web defaults) for both endpoints. The
serializer layer confirms camelCase. The list endpoint projects an anonymous object
`{ QuakeId, Magnitude, Place, City, Country, OccurredUtc }` → serialized camelCase → matches the
contract's `quakeId/magnitude/place/city/country/occurredUtc`. Full HTTP-response assertion belongs to
the Sprint 2 frontend pass (no frontend exists yet); recorded consistent at the contract/serializer level.

## Host boot / indexing — PASS (live)
Ran `func start --port 7073` independently (func CLI 4.12.0). Captured:
```
Functions:
    GetStoryCard: [GET] http://localhost:7073/api/cards/{quakeId}
    GetStoryCards: [GET] http://localhost:7073/api/cards
    StoryBuilderFunction: serviceBusTrigger
    UsgsPollerFunction: timerTrigger
```
All four functions index; HTTP routes resolve; SB + timer triggers bind. The only error logged is:
`The listener for 'StoryBuilderFunction' was unable to start. Azure.Messaging.ServiceBus: The
connection string could not be parsed` — this is the **placeholder** ServiceBusConnection, a config
condition, NOT a code defect (the trigger binds correctly given a real connection string). Confirmed
backend's boot report independently.

## e2e proof — EXECUTION-DEFERRED (infra unavailable), NOT a failure
Gate's e2e target: poller fires → message on `quake-events` → builder logs "Story card created" →
`curl /api/cards` returns the card. **Cannot run end-to-end here:**
- Docker engine NOT reachable (CLI present, daemon down) → no Service Bus emulator, no SQL, no Azurite.
- `local.settings.json` connections are placeholders; `UnsplashAccessKey` still pending from the user.
This exact dependency was anticipated in the plan (Task 19 / SPRINT-01 notes: emulators need Docker;
else point at a real Basic SB namespace + Azure SQL). What IS proven in lieu of the live run:
- Each hop's code verified: parser (live feed, pass #14), assembler failure-isolation (tests, #14),
  clients (B6, #15), dedup unique index (B5, #15), B1 wire round-trip (scripted), B3 blob round-trip
  (scripted), SQL write shape (StoryBuilderFunction maps every column; entity↔migration verified #15),
  API read path (GetAsync + projection), host indexes all four functions (live boot).
- The builder's success log string is present and correct: `"Story card created for M{Mag} {Place} ->
  {Blob}"` (StoryBuilderFunction.cs:52) — the exact gate signal, ready to emit on a real run.

**To close the deferral (Sprint 2 / when infra lands):** run against a real Basic Service Bus
namespace + Azure SQL (or `docker compose` once the daemon is available), trigger the poller, confirm
the "Story card created" log and a non-empty `curl /api/cards`. Tracked as a gate follow-up, not a defect.

## Defects
None. (Unsplash success-path remains static-only pending a key — carried from #15, not a pipeline defect.)
