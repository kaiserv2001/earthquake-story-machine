# QA Pass 01 — Skeleton (Wave 1.4)

**Task:** #4 · **Owner:** qa-engineer · **Date:** 2026-06-10
**Execution level:** Scripted (`dotnet build -warnaserror`) + static shape/dependency diff.
**Verdict: PASS** — skeleton is correct, no defects. Build clean, dependency rule honored,
models/interfaces match plan shapes, `QuakeJson.Options` pre-verified for the #20 B1 baseline.

> Scope note: by the time #3 unblocked me, backend had also landed #5/#6/#7/#8. This pass covers
> ONLY the skeleton scope (T1 projects/refs, T2 models, T3 interfaces, build). Assembler/parser
> TDD honesty + B7 is pass #14; migration B5 + clients B6 is pass #15.

## Plan verify step
`dotnet build` → "Build succeeded. 0 Warning(s) 0 Error(s)." — **MET** (ran with `-warnaserror`):
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```
All 4 projects emit: Quake.Core, Quake.Data, Quake.Functions, Quake.Core.Tests (+ generated
WorkerExtensions for the Functions host). dotnet SDK 10.0.108 builds the net8.0 targets fine.

## Checks

### 4 projects wired into the solution — PASS
`EarthquakeStoryMachine.sln` (classic format, intentional per SPRINT-01 note for EF/CI tooling).
Quake.Core, Quake.Data, Quake.Functions, tests/Quake.Core.Tests all build.

### Dependency rule (Functions→Core+Data, Data→Core, Tests→Core; NO back-ref) — PASS
Read every `<ProjectReference>`:
- Quake.Core.csproj — **no** project references (pure). ✔
- Quake.Data.csproj — → Quake.Core only. ✔
- Quake.Functions.csproj — → Quake.Core + Quake.Data. ✔
- Quake.Core.Tests.csproj — → Quake.Core only. ✔
No reference from Core or Data back to Functions. Core stays pure → assembler unit-testable. ✔

### TFM = net8.0 (env deviation recorded, not failed) — PASS w/ documented deviation
All four csproj `<TargetFramework>net8.0`. Env has only .NET 10 SDK (10.0.108), no .NET 8 runtime.
Backend added root `Directory.Build.props` with `<RollForward>Major</RollForward>` so net8.0 binaries
execute on the .NET 10 shared runtime. This is documented in SPRINT-01 note (2026-06-10, backend) and
in a comment in Directory.Build.props. **Per lead's instruction at #4: recorded as deviation, not a
fail.** Carry-forward for Sprint 2: infra/CI must NOT inherit RollForward assumptions into Bicep —
Azure Functions host supplies its own net8 runtime.

### Warnings-as-errors gate — PASS
TreatWarningsAsErrors=true on Core, Data, Functions. Test project omits it (conventional for test
projects; not a defect). Build green under explicit `-warnaserror` regardless.

### Models exist as sealed records, required init (T2) — PASS
- `QuakeEvent` (Models/QuakeEvent.cs): sealed record; required Id, Magnitude, Place, Latitude,
  Longitude, DepthKm, OccurredUtc; `string? Url` optional. Matches plan exactly.
- `StoryCard` (Models/StoryCard.cs): sealed record; required Quake + GeneratedUtc; nullable
  Location/Wiki/Weather/History (enrichment-may-fail semantics preserved); `Photos` defaults `[]`.
- Sub-records each in their own file (one-type-per-file convention): LocationInfo, WikiSummary,
  WeatherSnapshot, PhotoInfo, HistoricalContext. ✔

### Interfaces exist with correct signatures + null semantics (T3) — PASS
All six present in Quake.Core/Abstractions, one per file, namespace Quake.Core.Abstractions:
| Interface | Method | Return | Null contract |
|---|---|---|---|
| IGeocodingClient | ReverseGeocodeAsync(double lat,double lon,ct) | `Task<LocationInfo?>` | null = miss |
| IWikiClient | GetSummaryAsync(string title,ct) | `Task<WikiSummary?>` | null = miss |
| IWeatherClient | GetCurrentAsync(double lat,double lon,ct) | `Task<WeatherSnapshot?>` | null = miss |
| IPhotoClient | SearchAsync(string query,int count=3,ct) | `Task<IReadOnlyList<PhotoInfo>>` | **non-null** → empty on miss |
| IQuakeHistoryClient | GetHistoryAsync(double lat,double lon,DateTimeOffset before,ct) | `Task<HistoricalContext?>` | null = miss |
| IStoryCardStore | SaveAsync(StoryCard,ct) / GetAsync(string quakeId,ct) | `Task<string>` / `Task<StoryCard?>` | save non-null path; get null = absent |

All match plan Task 3 verbatim. `CancellationToken ct = default` on every method. ✔

### B1 baseline pre-verify (for #20) — QuakeJson.Options confirmed
`src/Quake.Core/QuakeJson.cs`: `public static class QuakeJson` exposing
`public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);`
— single shared static instance, Web defaults (camelCase). This is exactly the approved B1 spec
(lead, SPRINT-01 note: SB serialization uses shared QuakeJson.Options on both poller+builder sides).
**Locked as the #20 B1 baseline:** at #20 I will assert UsgsPollerFunction serializes and
StoryBuilderFunction deserializes via `QuakeJson.Options` (not bare `JsonSerializer`), and that the
blob store's Web defaults (B3) stay consistent with it.

## B6 interface baseline (recorded for pass #15 client diff)
The six signatures in the table above are the Side-A contract. At #15, each of the 5 enrichment
clients must match its interface signature AND its null/empty semantics — especially:
- IPhotoClient returns **empty list, not null** on failure (non-nullable return).
- IStoryCardStore.SaveAsync returns a non-null path string.

## Defects
None.

## Carry-forward watch-points
1. RollForward=Major must not leak into Sprint 2 Bicep/CI (Azure supplies net8 runtime).
2. #20 B1 baseline = QuakeJson.Options on both SB sides (locked above).
3. Test project has no TreatWarningsAsErrors — informational only.
