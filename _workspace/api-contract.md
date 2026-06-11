# Story Cards API — Contract

> Status: **FINAL** — task #19 (StoryCardsApiFunction) landed and the solution builds clean (commit pending).
> Owner: backend-engineer. Consumed by: frontend-engineer (Sprint 2 Static Web App).
> This shape is stable; changes go through backend-engineer.
> Serialization: all responses use `JsonSerializerDefaults.Web` (camelCase, case-insensitive).
> Note: the HTTP layer serializes via ASP.NET Core's MVC formatter (Web defaults), NOT
> `QuakeJson.Options` — they coincide on casing today, but a `QuakeJson.Options` naming-policy
> change would not affect API output. Service Bus messages and blob cards use `QuakeJson.Options`.
> (Clarified at Sprint-2 QA, pass 05 — behavior unchanged.)

Base path: the Functions app exposes routes under `/api`. The Static Web App linked
backend proxies `/api/*` to the Functions host, so the frontend calls `/api/cards`
and `/api/cards/{quakeId}` directly.

---

## GET /api/cards

Returns the 50 most recent quakes (by `occurredUtc` descending) as lightweight
summaries for the grid view. Source: SQL metadata table (not the blob cards).

- **Auth:** anonymous
- **200** `application/json` — array (possibly empty), newest first.

### Response shape (array item)

```json
[
  {
    "quakeId": "us7000srzt",
    "magnitude": 5.1,
    "place": "7 km NW of Nuing, Philippines",
    "city": "General Santos",
    "country": "Philippines",
    "occurredUtc": "2026-06-10T08:00:00+00:00"
  }
]
```

| Field | Type | Notes |
|---|---|---|
| `quakeId` | string | USGS event id; the key for the detail call below. |
| `magnitude` | number | Richter magnitude. |
| `place` | string | USGS human-readable label. |
| `city` | string \| null | Resolved city; `null` if geocoding missed (e.g. ocean epicenter). |
| `country` | string \| null | Resolved country; `null` if geocoding missed. |
| `occurredUtc` | string (ISO-8601) | Event time, UTC offset. |

Notes:
- Empty array (`[]`) when no cards exist yet — not a 404.
- `magnitude` badge color tiers (frontend): 4.5–5.4 amber, 5.5–6.4 orange, 6.5+ red.

---

## GET /api/cards/{quakeId}

Returns the full assembled story card for one quake. Source: blob store
(`story-cards/yyyy/MM/{quakeId}.json`).

- **Auth:** anonymous
- **Path param:** `quakeId` — the `quakeId` from the list response.
- **200** `application/json` — the full `StoryCard`.
- **404** — no card stored for that id.

### Response shape (full StoryCard)

Nullable sections (`location`, `wiki`, `weather`, `history`) are `null` when that
enrichment step failed or returned nothing — a dead API degrades the card, it never
removes the card. `photos` is always present but may be an empty array.

```json
{
  "quake": {
    "id": "us7000srzt",
    "magnitude": 5.1,
    "place": "7 km NW of Nuing, Philippines",
    "latitude": 5.6757,
    "longitude": 125.3874,
    "depthKm": 61.705,
    "occurredUtc": "2026-06-10T08:00:00+00:00",
    "url": "https://earthquake.usgs.gov/earthquakes/eventpage/us7000srzt"
  },
  "location": {
    "city": "General Santos",
    "region": "Soccsksargen",
    "country": "Philippines",
    "displayName": "General Santos, Soccsksargen, Philippines"
  },
  "wiki": {
    "title": "General Santos",
    "extract": "General Santos, officially the City of General Santos, is a city in the Philippines...",
    "pageUrl": "https://en.wikipedia.org/wiki/General_Santos",
    "thumbnailUrl": "https://upload.wikimedia.org/.../thumb.jpg"
  },
  "weather": {
    "temperatureC": 28.4,
    "windSpeedKmh": 11.2,
    "weatherCode": 2,
    "description": "Partly cloudy"
  },
  "photos": [
    {
      "imageUrl": "https://images.unsplash.com/photo-...&w=1080",
      "thumbUrl": "https://images.unsplash.com/photo-...&w=400",
      "photographerName": "Jane Doe",
      "photographerUrl": "https://unsplash.com/@janedoe"
    }
  ],
  "history": {
    "quakesLast30DaysWithin300Km": 7,
    "maxMagnitudeLastYear": 6.4
  },
  "generatedUtc": "2026-06-10T08:02:13.118+00:00"
}
```

### Field reference

`quake` (always present):

| Field | Type | Notes |
|---|---|---|
| `id` | string | USGS event id (same as `quakeId` in the list). |
| `magnitude` | number | |
| `place` | string | |
| `latitude` | number | |
| `longitude` | number | |
| `depthKm` | number | |
| `occurredUtc` | string (ISO-8601) | |
| `url` | string \| null | USGS event page. |

`location` (object \| null):

| Field | Type | Notes |
|---|---|---|
| `city` | string \| null | |
| `region` | string \| null | |
| `country` | string \| null | |
| `displayName` | string | Always a string when `location` is present (may be `""`). |

`wiki` (object \| null):

| Field | Type | Notes |
|---|---|---|
| `title` | string | |
| `extract` | string | May be `""`. |
| `pageUrl` | string \| null | |
| `thumbnailUrl` | string \| null | |

`weather` (object \| null):

| Field | Type | Notes |
|---|---|---|
| `temperatureC` | number | |
| `windSpeedKmh` | number | |
| `weatherCode` | number | WMO code; `-1` if unknown. |
| `description` | string | Human label for the WMO code. |

`photos` (array, never null; may be empty):

| Field | Type | Notes |
|---|---|---|
| `imageUrl` | string | Unsplash `regular` size. |
| `thumbUrl` | string | Unsplash `small` size. |
| `photographerName` | string | Attribution (Unsplash requires it). |
| `photographerUrl` | string | Link to the photographer profile. |

`history` (object \| null):

| Field | Type | Notes |
|---|---|---|
| `quakesLast30DaysWithin300Km` | number (int) | |
| `maxMagnitudeLastYear` | number \| null | `null` if no prior quakes in range. |

`generatedUtc` (string, ISO-8601, always present) — when the card was assembled.

---

## Casing & null rules (load-bearing for the frontend)

- All property names are **camelCase** (Web defaults). Do not expect PascalCase.
- Top-level enrichment sections may be `null`; the frontend must null-check
  `location`, `wiki`, `weather`, and `history` independently.
- `photos` is always an array; check `.length`, not null.
- Field values that come back as `null` from an API are preserved as `null`, not omitted.
