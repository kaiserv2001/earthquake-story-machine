---
name: enrichment-client-pattern
description: The mandatory pattern for ALL external API clients in the Earthquake Story Machine (Nominatim, Wikipedia, Open-Meteo, Unsplash, USGS). Read before creating, modifying, or debugging any class in src/Quake.Functions/Services/ that talks to an external HTTP API, and when adding a new enrichment source.
---

# Enrichment Client Pattern

Every enrichment client follows one shape. Uniformity is the point: the assembler treats all five identically, and a sixth source should be addable by copying the pattern.

## The shape
```csharp
public sealed class FooClient(HttpClient http) : IFooClient   // typed client, primary ctor
{
    public async Task<FooResult?> GetAsync(..., CancellationToken ct = default)
    {
        using var resp = await http.GetAsync(relativeUrl, ct);
        if (!resp.IsSuccessStatusCode) return null;            // never throw on API failure
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        // defensive parse: TryGetProperty for anything optional
        return new FooResult(...);
    }
}
```

Rules and the reasons behind them:
- **Return `null`/empty on failure, never throw.** The `StoryCardAssembler.Safe()` wrapper catches exceptions as a backstop, but a thrown exception is logged as a warning and pollutes telemetry; a clean `null` is the contract for "no data". A quake card with no photo is fine; a dead-lettered quake is not.
- **BaseAddress, auth headers, User-Agent live in `Program.cs` DI registration** — not in the client. The client only knows relative URLs. This keeps secrets and per-API etiquette in one auditable place.
- **`JsonDocument` + `TryGetProperty` for third-party JSON** (their schema, their changes); typed DTOs only for our own shapes. External APIs drop fields without notice — `GetProperty` on an optional field is a latent production crash.
- **Relative URLs with invariant formatting**: lat/lon as `{lat:F4}` (culture-safe), strings through `Uri.EscapeDataString`.
- **Honor `CancellationToken`** on every await — Functions pass real tokens on shutdown.

## Per-API etiquette (violating these gets the app blocked)
| API | Constraint |
|---|---|
| Nominatim | Identifying `User-Agent` mandatory; ≤1 req/sec; `zoom=10` for city-level |
| Unsplash | `Authorization: Client-ID <key>`; demo = 50 req/hr; must keep photographer attribution fields |
| Wikipedia | REST v1 `page/summary/{title}`; treat `type == "disambiguation"` as miss |
| Open-Meteo | No key; `current=` params are comma-joined, response mirrors them |
| USGS FDSN | `count` endpoint for counts (cheap), `query&limit=1&orderby=magnitude` for max |

## Verification per client
1. One real request against the live API (curl), response saved (redacted) to `_workspace/03_enrichment_smoke.md`.
2. Compare live field names against your parse — Nominatim especially: city may arrive as `city`, `town`, `village`, `municipality`, or `county`; ocean epicenters have **no** `address` at all. Handle the miss, it's the common case for quakes.
