# QA Pass 05 — Q.1 (re-run) Boundary B2: API response ↔ `frontend/app.js` field access

**Owner:** qa-engineer · **Trigger:** Sprint 2 resume (Q.1 was in_progress at pause — re-run from scratch)
**Verdict: PASS** — boundary holds in both directions; casing now confirmed at **runtime** (not just docs).
**Level:** Static (both sides read & diffed this pass) **+ Scripted** (real-type serialization probe through the
formatter default). Full live HTTP curl is folded into Q.3 e2e.

## Sides compared (read fresh this pass)
- **Side A (API emit):** `src/Quake.Functions/Functions/StoryCardsApiFunction.cs` (lines 12–33) +
  `src/Quake.Functions/Program.cs` (no JSON-options override) + the record shapes in `src/Quake.Core/Models/`
  (`StoryCard`, `QuakeEvent`, `PhotoInfo`, `HistoricalContext`, etc.). Contract: `_workspace/api-contract.md`.
- **Side B (frontend reads):** `frontend/app.js` (all 470 lines read) — every `summary.*`, `card.*`, `quake.*`,
  `photo.*`, `weather.*`, `wiki.*`, `history.*` access.

## Casing — the carry-over risk, now resolved at runtime
The list endpoint returns an **anonymous PascalCase** projection (`new { s.QuakeId, s.Magnitude, … }`,
StoryCardsApiFunction.cs:20) and detail returns the `StoryCard` record — both via `OkObjectResult`
(`IActionResult`). The isolated-worker `Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore`
integration (`ConfigureFunctionsWebApplication`, Program.cs:13) serializes `IActionResult` through ASP.NET
Core's **MVC output formatter**, whose default is `JsonSerializerDefaults.Web` → camelCase. Verified there is
**no** `AddJsonOptions` / `AddMvc` / `ConfigureHttpJsonOptions` override and **no** `[JsonPropertyName]` attribute
anywhere in `src/Quake.Functions` or `src/Quake.Core/Models` (grep clean).

**Runtime confirmation (this pass):** a temporary xUnit probe in `Quake.Core.Tests` serialized the real
`StoryCard` record and the anonymous list projection through `new JsonSerializerOptions(JsonSerializerDefaults.Web)`
— the exact formatter default — and asserted every key app.js reads is present in camelCase, with PascalCase
negative controls (`"Magnitude"`, `"DepthKm"`, `"QuakeId"`) absent. **Both probe tests passed (2/2).** Probe
then removed; full suite back to **9/9 green**. This upgrades the prior pass's docs-only casing claim to scripted.

> Not a full live HTTP curl (that needs SQL + blob, deferred to Q.3), but it exercises the real model types
> through the identical serializer configuration the live formatter uses. The only thing Q.3 adds is the HTTP
> transport itself.

## List view (`GET /api/cards` → buildCardTile/renderList)
| app.js access | line | API field | Result |
|---|---|---|---|
| `summary.quakeId` | 191 | `quakeId` | ✓ |
| `summary.magnitude` | 195 | `magnitude` | ✓ |
| `summary.occurredUtc` | 199,200 | `occurredUtc` | ✓ |
| `summary.place` | 205 | `place` | ✓ |
| `summary.city` (nullable) | 208 | `city` — guarded by `.filter(Boolean)` | ✓ null-safe |
| `summary.country` (nullable) | 208 | `country` — guarded by `.filter(Boolean)` | ✓ null-safe |

Empty array → `renderEmpty()` (line 229/230, `!Array.isArray || length === 0`). Matches contract "`[]` not 404".

## Detail view (`GET /api/cards/{id}` → renderDetail + builders)
| app.js access | line | API field / nullability | Result |
|---|---|---|---|
| `card.photos` | 432,444 | array, never null — `Array.isArray` + `length` guards (248,371) | ✓ |
| `card.quake` | 436,437 | always present (`required`) | ✓ |
| `card.weather` | 441 | nullable — `if(!weather) return null` (317) | ✓ independent null-check |
| `card.wiki` | 442 | nullable — `if(!wiki||!wiki.extract) return null` (330) | ✓ |
| `card.history` | 443 | nullable — `if(!history) return null` (348) | ✓ |
| `quake.magnitude/place/depthKm/occurredUtc/latitude/longitude` | 282–295 | all `required` | ✓ |
| `quake.url` (nullable) | 304 | `if(quake.url)` guard | ✓ |
| `photo.imageUrl/thumbUrl/photographerName/photographerUrl` | 256–390 | all present; `thumbUrl||imageUrl` fallback (380) | ✓ |
| `weather.temperatureC/windSpeedKmh/description` | 319–324 | present; `description` guarded | ✓ |
| `wiki.title/extract/pageUrl` | 332–334 | present; `pageUrl` guarded; `title||fallback` | ✓ |
| `history.quakesLast30DaysWithin300Km` | 350 | present (int) | ✓ |
| `history.maxMagnitudeLastYear` (nullable) | 358 | `!= null` guard | ✓ |

All four nullable sections (location, wiki, weather, history) are checked **independently** — one dead
enrichment never breaks the card. `photos` checked by `Array.isArray`/`.length`, never null. Matches the
contract's load-bearing null rules exactly.

Note: `app.js` does not read `card.location` at all (uses `quake.place` + list-level city/country). That is an
*unused* contract field, not a mismatch — no defect.

## Defects
**None.** Boundary holds in both directions; casing confirmed at runtime.

## Informational note (NOT a defect, no code change) — for backend-engineer / gate
`_workspace/api-contract.md` (lines 6–7, 178–179) says responses serialize "via `QuakeJson.Options`." That is
inaccurate for the HTTP layer: `OkObjectResult` uses ASP.NET Core's MVC formatter, **not** `QuakeJson.Options`.
The two **coincide on casing** (both Web defaults), so output is correct today. Latent risk: if anyone later sets
`QuakeJson.Options.PropertyNamingPolicy` expecting it to change API output, it silently won't — the API would
need `.AddMvc().AddJsonOptions(...)` / `ConfigureHttpJsonOptions`. Suggest a one-line contract clarification.
Carried to the Sprint 2 gate as an informational item.
