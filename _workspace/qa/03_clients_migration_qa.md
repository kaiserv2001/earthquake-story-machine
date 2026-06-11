# QA Pass 03 — Clients B6 + Migration B5 (Wave 2.11 / 2.12)

**Task:** #15 · **Owner:** qa-engineer · **Date:** 2026-06-10
**Execution level:** Scripted (`dotnet build -warnaserror` 0/0; `dotnet ef migrations list`) +
static both-sides interface↔impl diff + GetProperty-vs-TryGetProperty grep sweep.
**Verdict: PASS (one open verification caveat)** — all five enrichment clients honor their interface
signatures AND null/empty semantics; every nested JSON access is defensive; migration B5 has the
unique QuakeId dedup index with correct types/lengths/nullability. No defects. **Caveat: Unsplash
success-path parse is verified against the DOCUMENTED shape only, not a live 200 response — no API
key available (live call returns 401). Re-verify when a key is provisioned (open TODO, not a defect).**
Process notes below (not defects).

> Board note: at write time task #13 (UsgsHistoryClient) still showed in_progress, but
> `UsgsHistoryClient.cs` has landed complete and builds clean — I verified it. Folding migration B5
> (pre-checked at lead's request while #15 was blocked) into this single report.

## Build
`dotnet build -warnaserror` → Build succeeded, 0 Warning(s), 0 Error(s), all clients compile.

## B6 — Core interfaces ↔ five client implementations
Read each interface (Side A, `Quake.Core/Abstractions`) against each impl (Side B,
`Quake.Functions/Services`) — signature, return type, AND null/empty contract:

| Client | Interface | Signature match | Failure return | Verdict |
|---|---|---|---|---|
| NominatimClient | IGeocodingClient.ReverseGeocodeAsync(lat,lon,ct)→`LocationInfo?` | ✔ | `null` on !success AND missing `address` (ocean epicenter — the common quake case) | PASS |
| WikipediaClient | IWikiClient.GetSummaryAsync(title,ct)→`WikiSummary?` | ✔ | `null` on !success (404) AND on `type=="disambiguation"` | PASS |
| OpenMeteoClient | IWeatherClient.GetCurrentAsync(lat,lon,ct)→`WeatherSnapshot?` | ✔ | `null` on !success AND missing `current` object | PASS |
| UnsplashClient | IPhotoClient.SearchAsync(query,count=3,ct)→`IReadOnlyList<PhotoInfo>` | ✔ | **empty `[]`** (NOT null) on !success AND missing `results` — honors non-nullable contract | PASS (success-path static-only, see caveat) |
| UsgsHistoryClient | IQuakeHistoryClient.GetHistoryAsync(lat,lon,before,ct)→`HistoricalContext?` | ✔ | `null` only on count-call !success; partial OK (count w/ null maxMag) | PASS |

**Pattern rule — "never throw on API failure" (enrichment-client-pattern):** all five return
`null`/`[]` on `!IsSuccessStatusCode`; none throws on an API miss. The assembler's `Safe()` is a
backstop, but the clients don't lean on it. ✔

### Verification level per client (smoke: `_workspace/03_enrichment_smoke.md`)
enrichment-engineer captured one live request per API. Four are **live-verified** against real
responses; Unsplash is **doc-shape-only**:
- Nominatim — LIVE (Tokyo land hit AND mid-Pacific ocean `{"error":"Unable to geocode"}` → null). ✔
- Wikipedia — LIVE (standard page, disambiguation→null, 404→null). ✔
- Open-Meteo — LIVE (current object parsed; WMO code→description). ✔
- USGS FDSN — LIVE (count + max-mag queries). ✔
- **Unsplash — UNVERIFIED against a live 200.** No API key available; live call returns HTTP 401.
  Client was written against the documented `GET /search/photos` shape and returns `[]` on 401 (so a
  missing key degrades the card to no-photos, never fails it — that failure path IS correct and
  matters most). My B6 read confirms the success-path parse matches the documented
  `results[].urls.{regular,small}` + `user.{name,links.html}` shape and preserves photographer
  attribution per Unsplash terms — but no real 200 body has been observed, so the success parse is
  **static-only**. **Open TODO (not a defect):** re-run with a real `Client-ID` key once provisioned
  and confirm the live field names match. I'll fold this re-verify into #20 IF a key lands before the
  gate; otherwise the gate records Unsplash success-path as static-only and the photos section as a
  known-degraded-without-key path. The Unsplash key also feeds B4 (Program.cs `cfg["UnsplashAccessKey"]`
  ↔ local.settings.json) — tracked there.

### GetProperty-vs-TryGetProperty sweep (lead-requested, extends #14 notes)
The landed clients are MORE defensive than the plan's reference code — every nested/optional access
was hardened from bare `GetProperty` to `TryGetProperty`. Latent-crash audit per client:
- **Nominatim:** `address` guarded (return null if absent); each address key via `TryGetProperty`;
  `display_name` guarded. No bare GetProperty on optional fields. ✔
- **Wikipedia:** deep chain `content_urls.desktop.page` and `thumbnail.source` fully guarded
  link-by-link; `title`/`extract` guarded with fallbacks. ✔
- **OpenMeteo:** `current`, `weather_code`, `temperature_2m`, `wind_speed_10m` all guarded;
  unknown weather_code → -1 → "Unknown" (no dictionary KeyNotFound). ✔
- **Unsplash:** `results`, and per-item `urls`/`user`/`links` guarded via a `Str()` helper that
  checks `ValueKind==Object` before reading — handles items missing nested objects. ✔
- **UsgsHistory:** `count` guarded; the max-mag chain `features[0].properties.mag` guarded link-by-
  link AND checks `magEl.ValueKind==Number` — so a **JSON-null mag** (the B7-class crash the plan's
  bare `GetDouble()` would have hit) is handled. Date params use `CultureInfo.InvariantCulture`. ✔

No bare `GetProperty` on an optional/third-party field remains in any client. The two #14
informational notes are answered for the client layer: integer-vs-decimal numbers are read via
`GetDouble`/`GetInt32` (token-agnostic), and there is no null-forgiving `!` on third-party fields.

### Client etiquette (relative URLs, no in-client secrets)
- Grep confirms NO `http(s)://` literal and NO `BaseAddress`/`Authorization`/`UserAgent`/
  `DefaultRequestHeaders` assignment inside any client (one Unsplash match is a comment). All use
  relative URLs only — BaseAddress/auth/User-Agent must be set in Program.cs DI (audited at #16).
- Every `await` passes `ct` (Nominatim, Wikipedia, OpenMeteo, Unsplash, UsgsHistory, BlobStore). ✔
- lat/lon formatted `{:F4}` (culture-safe); query strings via `Uri.EscapeDataString`. ✔

### BlobStoryCardStore (B3-relevant, included for completeness)
`BlobStoryCardStore : IStoryCardStore` — SaveAsync→`Task<string>` (non-null path), GetAsync→
`Task<StoryCard?>`. Uses `new(QuakeJson.Options){WriteIndented=true}` — a CLONE of the shared Web
options, SAME options object used in BOTH save (Serialize) and get (Deserialize) directions. This is
the correct B3 shape and is consistent with the QuakeJson baseline locked at #4. Carried forward to
#20 as confirmed B3-clean. ✔

## B5 — EF entity + migration ↔ SQL schema (pre-checked 2026-06-10)
Verified entity (`StoryCardRecord`) ↔ `QuakeDbContext.OnModelCreating` ↔ Initial migration `Up()` ↔
`QuakeDbContextModelSnapshot` — all three agree.
Scripted: `dotnet ef migrations list -p src/Quake.Data -s src/Quake.Data` →
`20260610105304_Initial` (via DesignTimeQuakeDbContextFactory; the SQL-connect error is expected,
no server running — migration metadata loads fine).

- **Unique index on QuakeId** — `CreateIndex IX_StoryCards_QuakeId ... unique: true`; snapshot
  `HasIndex("QuakeId").IsUnique()`. **This is the dedup guarantee** for poller+builder idempotency.
  Present and correct. ✔
- Max lengths: QuakeId `nvarchar(64)`, Place `nvarchar(256)`, BlobPath `nvarchar(512)` — match config. ✔
- NOT NULL: QuakeId, Place, BlobPath `nullable: false` (+ IsRequired in snapshot). ✔
- Nullable: City, Country `nvarchar(max)`, `nullable: true` (display-only metadata, no key role). ✔
- Types: Id int identity(1,1) PK; Magnitude/Latitude/Longitude `float`; OccurredUtc/CreatedUtc
  `datetimeoffset`. ✔

## Defects
None.

## Process notes (NOT defects — for the team/Sprint 2)
1. **EF migrations command form:** the plan's `dotnet ef ... -s src/Quake.Functions` FAILS — the
   Functions startup project doesn't reference `Microsoft.EntityFrameworkCore.Design`. Use
   `-s src/Quake.Data` (the sanctioned DesignTimeQuakeDbContextFactory handles it), OR add the Design
   package to Functions. Anyone adding a future migration must use the `-s src/Quake.Data` form.
2. **B3 carry-forward to #20:** BlobStoryCardStore options confirmed consistent with QuakeJson.Options.
   At #20 I'll assert the poller/builder SB serialization (B1) lands on the same QuakeJson.Options.
3. Board status lag: #13 verified complete despite showing in_progress — flagging to lead for sync.
