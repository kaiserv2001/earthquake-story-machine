# Enrichment Client Smoke Checks

One live request per external API, captured during client implementation. Secrets redacted.
Clients use relative URLs only; BaseAddress / auth / User-Agent are configured in `Program.cs` (backend task #16).

---

## Nominatim — `NominatimClient` (`IGeocodingClient`) — VERIFIED

**Constraints honored:** identifying `User-Agent` sent, `zoom=10` (city-level), `format=jsonv2`. Policy: ≤1 req/sec.

**Request (land epicenter, Tokyo):**
```
curl -H "User-Agent: EarthquakeStoryMachine/1.0 (portfolio project)" \
  "https://nominatim.openstreetmap.org/reverse?lat=35.6895&lon=139.6917&format=jsonv2&zoom=10&accept-language=en"
```

**Response (trimmed):**
```json
{
  "osm_type": "relation",
  "addresstype": "city",
  "name": "Shinjuku",
  "display_name": "Shinjuku, Tokyo, 160-8484, Japan",
  "address": {
    "city": "Shinjuku",
    "ISO3166-2-lvl4": "JP-13",
    "postcode": "160-8484",
    "country": "Japan",
    "country_code": "jp"
  }
}
```
Parsed → `LocationInfo(City: "Shinjuku", Region: null, Country: "Japan", DisplayName: "Shinjuku, Tokyo, 160-8484, Japan")`.
Note: this response has `city` but no `state` — Region is legitimately null. The `Get("city","town","village","municipality","county")` fallback chain covers the variants seen in other regions.

**Request (ocean epicenter, mid-Pacific):**
```
curl -H "User-Agent: EarthquakeStoryMachine/1.0 (portfolio project)" \
  "https://nominatim.openstreetmap.org/reverse?lat=0.0&lon=-150.0&format=jsonv2&zoom=10&accept-language=en"
```

**Response:**
```json
{ "error": "Unable to geocode" }
```
No `address` property → client returns `null`. This is the common case for quakes (ocean epicenters); handled, not an error.

---

## Wikipedia — `WikipediaClient` (`IWikiClient`) — VERIFIED

**Constraints honored:** REST v1 `page/summary/{title}`, `redirect=true`, `type == "disambiguation"` treated as a miss.

**Request (standard page):**
```
curl "https://en.wikipedia.org/api/rest_v1/page/summary/Tokyo?redirect=true"
```

**Response (trimmed):**
```json
{
  "type": "standard",
  "title": "Tokyo",
  "thumbnail": { "source": "https://upload.wikimedia.org/.../330px-Skyscrapers_of_Shinjuku_2009_January.jpg" },
  "extract": "Tokyo, officially the Tokyo Metropolis, is the ...",
  "content_urls": { "desktop": { "page": "https://en.wikipedia.org/wiki/Tokyo" } }
}
```
Parsed → `WikiSummary(Title: "Tokyo", Extract: "Tokyo, officially ...", PageUrl: "https://en.wikipedia.org/wiki/Tokyo", ThumbnailUrl: ".../330px-...jpg")`.

**Disambiguation case:** `GET .../page/summary/Mercury?redirect=true` → `"type": "disambiguation"` → client returns `null` (miss).

**Miss case:** `GET .../page/summary/Zxqwvb_no_such_place_12345` → HTTP 404 → client returns `null`.
