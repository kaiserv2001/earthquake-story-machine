# QA Pre-Staged Boundary Checklists — Sprint 1

Pre-staged while blocked on upstream tasks. Each pass below maps a QA task to the exact
boundary rows, the two artifacts to read, and the assertion to make. **Existence ≠ correctness:
read BOTH sides and diff shapes.** Execution level labeled honestly per pass.

Environment (2026-06-10): dotnet SDK **10.0.108** present; plan targets **net8.0** (watch for
SDK/TFM friction). No `func` CLI / Docker confirmed yet → Wave 3 e2e likely degrades to
**scripted/static**; label the level in every report.

---

## Pass #4 — Skeleton QA (unblocks after #3 interfaces)
**Plan verify:** `dotnet build` → "Build succeeded. 0 Warning(s) 0 Error(s)."
**Execution target:** Scripted (`dotnet build` whole solution from repo root).

Checklist:
- [ ] `dotnet build` whole solution: 0 errors, 0 warnings (conventions: warnings-as-errors gate).
- [ ] Solution has exactly 4 projects wired: Quake.Core, Quake.Data, Quake.Functions, tests.
- [ ] **Dependency rule** (dotnet-conventions): Functions→Core+Data, Data→Core, Tests→Core.
      NO reference from Core/Data → Functions. Read each .csproj `<ProjectReference>` and assert direction.
- [ ] TFM: each csproj targets net8.0 as plan states (or note deviation if backend used net10 to match SDK).
- [ ] Models exist as `sealed record` with `required init` (T2): QuakeEvent, StoryCard + sub-records.
- [ ] Interfaces exist (T3): IGeocodingClient, IWikiClient, IWeatherClient, IPhotoClient,
      IQuakeHistoryClient, IStoryCardStore — signatures match plan (null semantics: `Task<X?>`).
- [ ] B6 pre-check: note interface signatures verbatim so client pass #15 can diff against them.

---

## Pass #14 — Core TDD honesty + B7 (unblocks after #5 assembler, #6 parser)
**Plan verify:** tests written first (red), then green. B7 = fixture vs live USGS feed.
**Execution target:** Scripted (`dotnet test`) + static fixture-vs-live diff.

TDD honesty (do NOT just see green — confirm the tests are real):
- [ ] StoryCardAssemblerTests: assert the 3 plan cases exist and actually assert, not `Assert.True(true)`:
      (a) all clients succeed → full card (every section non-null);
      (b) wiki client THROWS → card still produced, `Wiki == null` (failure isolation);
      (c) geocoder returns null → wiki/photo query falls back to `QuakeEvent.Place` (verify the
          mock is called WITH quake.Place, not with null/empty).
- [ ] Assembler runs enrichment in parallel (Task.WhenAll) — not sequential await chain.
- [ ] `Safe()` swallows exceptions → returns default; confirm test (b) would FAIL if Safe were removed
      (i.e. the test genuinely exercises isolation, not a no-op).

**B7 — fixture vs live feed (the headline risk):**
- [ ] Read the test fixture JSON used by UsgsFeedParserTests.
- [ ] Fetch (or compare against) a real USGS 4.5_day.geojson sample. Assert the fixture contains a
      feature with `properties.mag == null` (JSON null, not 0). Plan parser guards mag null →
      the FIXTURE must actually exercise that branch or the guard is untested.
- [ ] Assert fixture has: features[].properties.{mag,place,time,url}, geometry.coordinates[lon,lat,depth].
- [ ] Live-feed field presence: `place` can be null? `url` optional (parser uses TryGetProperty — good).
      `time` is unix ms int64. Confirm parser uses FromUnixTimeMilliseconds (not seconds).
- [ ] minMagnitude filter: feature with mag < threshold is dropped; == threshold kept (boundary value).

---

## Pass #15 — Clients B6 + Migration B5 (unblocks after #7,#9,#10,#11,#12,#13)
**Execution target:** Scripted (`dotnet build` clients) + static interface/SQL diff +
optional live curl smoke per enrichment-client-pattern.

**B6 — Core interfaces ↔ 5 client implementations:**
For EACH client read the interface (Side A, Quake.Core/Abstractions) and impl (Side B,
Quake.Functions/Services) and diff:
- [ ] NominatimClient : IGeocodingClient — ReverseGeocodeAsync(lat,lon,ct) → `LocationInfo?`.
      null semantics: returns null on !IsSuccess AND when no `address` (ocean epicenter — common!).
- [ ] WikipediaClient : IWikiClient — GetSummaryAsync(title,ct) → `WikiSummary?`.
      disambiguation → null (contract miss, per pattern skill).
- [ ] OpenMeteoClient : IWeatherClient — GetCurrentAsync(lat,lon,ct) → `WeatherSnapshot?`.
- [ ] UnsplashClient : IPhotoClient — SearchAsync(query,count,ct) → `IReadOnlyList<PhotoInfo>`
      returns EMPTY list (`[]`) on failure, NOT null (interface is non-nullable list!). Critical:
      assembler does `photosTask.Result ?? []` so null would also survive, but contract says empty.
- [ ] UsgsHistoryClient : IQuakeHistoryClient — GetHistoryAsync(lat,lon,before,ct) → `HistoricalContext?`.
- [ ] **Pattern rule (enrichment-client-pattern): never throw on API failure → return null/empty.**
      Grep each client for `GetProperty` on optional fields (latent crash) vs TryGetProperty.
      Especially Nominatim address keys, Unsplash nested urls/user/links, history mag-null.
- [ ] BaseAddress/auth/User-Agent must live in Program.cs DI, NOT in client (audit when #16 lands;
      here just confirm clients use relative URLs only).
- [ ] CancellationToken honored on every await.

**B5 — EF entity + migration ↔ intended SQL schema — DONE 2026-06-10 (pre-check), PASS:**
Verified across StoryCardRecord entity + QuakeDbContext.OnModelCreating + Initial migration Up() +
ModelSnapshot — all three agree. Scripted: `dotnet ef migrations list -p src/Quake.Data -s
src/Quake.Data` lists `20260610105304_Initial` (read via DesignTimeQuakeDbContextFactory; SQL-connect
error is expected/harmless, no server running — migration metadata loads fine).
- [x] **Unique index on QuakeId** — migration Up(): CreateIndex IX_StoryCards_QuakeId, `unique: true`.
      Snapshot: `b.HasIndex("QuakeId").IsUnique()`. DEDUP GUARANTEE present. ✔
- [x] Max lengths match config: QuakeId nvarchar(64), Place nvarchar(256), BlobPath nvarchar(512). ✔
- [x] required (QuakeId, Place, BlobPath) → `nullable: false` (NOT NULL) + IsRequired in snapshot. ✔
- [x] nullable City/Country → `nvarchar(max)`, `nullable: true` (no HasMaxLength set — acceptable,
      they're display-only metadata, not keys). ✔
- [x] PK Id int identity(1,1); Magnitude/Lat/Lon float; OccurredUtc/CreatedUtc datetimeoffset. ✔
- NOTE (process, not a defect): `dotnet ef ... -s src/Quake.Functions` FAILS because the Functions
  startup project doesn't reference Microsoft.EntityFrameworkCore.Design — the plan's command form
  (`-s src/Quake.Functions`) won't work as written. The DesignTimeQuakeDbContextFactory in Quake.Data
  is the sanctioned fallback (SPRINT-01 note, backend #7) and works with `-s src/Quake.Data`. Flag for
  anyone adding future migrations: use `-s src/Quake.Data`, OR add the Design pkg to Functions.

---

## Pass #20 — Pipeline B1/B3/B4 + e2e proof + Sprint 1 gate (unblocks after #17,#18,#19)
**Execution target:** Live (`func start`) if CLI+emulators available; else scripted/static — LABEL HONESTLY.

**B1 — poller serialize ↔ builder deserialize (THE classic null-fields bug):**
**APPROVED SPEC (lead, 2026-06-10):** backend-engineer will centralize `QuakeJson.Options`
(Web defaults) in Quake.Core and use it on BOTH sides of the Service Bus boundary in #17/#18.
This is the baseline at #20 — assert landed code matches it; ANY divergence is a defect.
- [ ] `Quake.Core` exposes `QuakeJson.Options` = `new JsonSerializerOptions(JsonSerializerDefaults.Web)`
      (single shared instance, per conventions "centralize if used 3+ places").
- [ ] UsgsPollerFunction serializes QuakeEvent WITH `QuakeJson.Options` (NOT bare `Serialize(q)`).
- [ ] StoryBuilderFunction deserializes WITH `QuakeJson.Options` (NOT bare `Deserialize<QuakeEvent>`).
- [ ] Both sides reference the SAME options → casing agrees. If either side still uses a bare/default
      serializer, or the two diverge → **every QuakeEvent field null, no exception** → file B1 defect
      to backend-engineer immediately (blocks e2e).
- [ ] Cross-check B3: BlobStoryCardStore Web defaults now consistent with the SB boundary too.
- [ ] Assert round-trip: serialize a QuakeEvent with poller's options, deserialize with builder's
      options → all `required` fields populated (write a tiny scripted round-trip if no runtime).

**B3 — StoryCard ↔ blob JSON ↔ API GetAsync deserialize:**
- [ ] BlobStoryCardStore.SaveAsync uses Web+indented; GetAsync uses SAME `Json` field. Confirm same
      options both directions (read the static readonly field, assert one shared instance).
- [ ] API GetStoryCard returns store.GetAsync result → round-trips through the same options.

**B4 — local.settings.json keys ↔ Program.cs cfg[] reads ↔ (Bicep later):**
- [ ] Enumerate every `cfg["..."]` in Program.cs: BlobStorageConnection, SqlConnection,
      UnsplashAccessKey, ServiceBusConnection (binding), UsgsPollSchedule, UsgsMinMagnitude.
- [ ] Assert each key EXACTLY present in local.settings.json Values (string match, case-sensitive).
      Classic: works locally, null in Azure — here just local↔Program.cs; Bicep is Sprint 2.
- [ ] Connection attribute names on triggers match settings keys: ServiceBusConnection.

**e2e proof (gate requirement):** poller fires locally → message on `quake-events` queue →
builder logs "Story card created" → `curl http://localhost:7071/api/cards` returns the card.
- [ ] If func CLI + Service Bus emulator + Azurite + SQL available → run it live, capture logs+curl.
- [ ] If NOT available → degrade: scripted unit/round-trip proof of each hop + STATIC trace of the
      wiring, and state in the gate file that e2e was static-only and WHY (no emulator/CLI).

**Gate file `_workspace/qa/gate_sprint1.md`:** all 7 matrix rows status, open defects by severity,
GO/NO-GO. Then SendMessage verdict to team-lead.

---

## Standing watch-points (carry across passes)
1. **JSON options coherence** is the spine of this app. Three serialization sites must agree where
   they meet: SB message (B1), blob card (B3), HTTP API (B2/Sprint-frontend). The conventions skill
   mandates Web defaults everywhere; the plan's poller/builder use bare defaults. Track which wins in
   landed code — silent null-field bugs produce NO exception.
2. **Dedup unique index (B5)** is the only thing preventing duplicate cards across poller runs.
3. **Failure isolation (assembler Safe)** — one dead API must degrade, never dead-letter.
4. **Ocean epicenters** (no Nominatim address) are the COMMON case for quakes, not the edge case.
5. **net8.0 vs SDK 10** — confirm builds actually target 8.0 or note the deviation.
