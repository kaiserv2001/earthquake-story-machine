# QA Pass 02 — Core TDD honesty + B7 (Wave 2.10)

**Task:** #14 · **Owner:** qa-engineer · **Date:** 2026-06-10
**Execution level:** Scripted (`dotnet test`, 9/9 green) + **Live B7** (fetched real USGS
4.5_day.geojson, HTTP 200, 21 features) diffed against the test fixture.
**Verdict: PASS** — assembler + parser tests are honest and substantive; fixture is faithful to
the live feed and correctly exercises the null-mag branch. No defects. Two informational notes below.

Covers: StoryCardAssembler (T4/#5) test honesty + failure isolation; UsgsFeedParser (T5/#6)
fixture-vs-live (B7). Migration B5 and client B6 are pass #15 (not in scope here).

## `dotnet test`
```
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9 - Quake.Core.Tests.dll (net8.0)
```

## TDD honesty — StoryCardAssemblerTests (4 tests, all real assertions)
Read every test; none is a `Assert.True(true)` no-op. Each asserts observable behavior:

- **All_clients_succeed_produces_full_card** — asserts every section non-null AND that the chosen
  enrichment *subject* is the geocoded city: `wiki.LastTitle == "Sample City"`, `photos.LastQuery
  == "Sample City"`. This verifies the city→region→Place selection logic, not just "card exists".
- **Wiki_client_throws_card_still_produced_with_null_wiki** — FakeWiki throws; asserts `card.Wiki
  == null` AND that Location/Weather/History remain non-null. This genuinely exercises failure
  isolation: if `Safe()` were removed, the throw would propagate and the test would fail (the fake
  throws a real InvalidOperationException). Plan case (b) satisfied.
- **Geocoder_null_falls_back_to_quake_place_for_queries** — geocoder returns null; asserts
  `wiki.LastTitle == quake.Place` and `photos.LastQuery == quake.Place` (the fakes record the
  argument, so this proves the fallback string actually reached the clients, not just that the card
  built). Plan case (c) satisfied.
- **Geocoder_throws_card_still_produced_with_null_location** — bonus beyond the plan's 3 cases:
  geocoder *throws* (vs returns null) and the card still falls back to Place. Good — covers the
  throw path of the first await, which precedes the Task.WhenAll fan-out.

**Parallel fan-out confirmed:** assembler builds wikiTask/weatherTask/photosTask/historyTask then
`await Task.WhenAll(...)` — not a sequential await chain. The geocode runs first (sequential) because
the subject feeds the other queries; that's correct, not a defect.

**Fakes (Fakes.cs):** test doubles implement the real interfaces, record the call argument
(LastTitle/LastQuery), and support a `Throw` flag. The photos fake returns the non-null
`IReadOnlyList<PhotoInfo>` per the interface contract. Doubles are honest — no over-stubbing that
would mask a logic gap.

> Note: the plan's step 2 ("dotnet test → fails (red)") is a process step I can't retro-verify
> after the fact (work already committed green). The tests as written would genuinely fail against a
> no-op or exception-propagating assembler, which is the property that matters — they are not
> tautological. I treat TDD-honesty as satisfied on that basis.

## B7 — fixture vs LIVE feed (the headline check)
Fetched `https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/4.5_day.geojson` live
(HTTP 200, 21 features). Trimmed sample saved: `_workspace/qa/_usgs_live_sample_2026-06-10.json`.
Compared every field the parser reads against the live feed across all 21 features:

| Field parser reads | Access | Live feed reality | Verdict |
|---|---|---|---|
| `feature.id` | GetProperty.GetString()! | present, string, all 21 | OK |
| `properties.mag` | guarded null→0, else GetDouble | present all 21; **0 nulls live** but can be JSON null (fixture proves it) | OK |
| `properties.place` | GetProperty.GetString() ?? "Unknown" | present all 21, never JSON-null in this sample | OK |
| `properties.time` | GetProperty.GetInt64() | present, int (unix **ms**), e.g. 1781089840128 | OK (ms, matches FromUnixTimeMilliseconds) |
| `geometry.coordinates[0..2]` | index .GetDouble() | all 21 have exactly length-3 arrays | OK |
| `properties.url` | **TryGetProperty** (defensive) | present in live, but fixture's null-mag feature omits it → defensive read correct | OK |

**Fixture faithfulness:** the test fixture's 3 features mirror the live shape (same property nesting,
coordinates [lon,lat,depth], unix-ms time) and—crucially—include a `"mag": null` feature that the
LIVE feed did not contain in this capture. That is exactly right: the null-mag branch (the classic
B7 "passes on fixture, crashes on real feed") can't be relied on to appear in any given live pull, so
the fixture MUST carry it, and it does. `Null_magnitude_is_treated_as_zero_and_excluded` asserts the
null-mag feature is dropped without throwing. B7 satisfied.

## Informational notes (NOT defects)
1. **Integer magnitudes in live data:** live `mag` values include bare integers (`5`, not `5.0`).
   The parser's `GetProperty("mag").GetDouble()` handles these correctly — System.Text.Json reads any
   JSON number token as a double. The fixture only contains decimal mags (5.1, 4.2), so the
   integer-mag path is covered by live behavior, not by a fixture case. No bug; flagging only so a
   future fixture edit doesn't assume mags are always decimals. (Could add an integer-mag fixture
   feature for completeness — optional, low value.)
2. **`id` uses null-forgiving `GetString()!`:** if a future feed ever omitted `id`, this would NPE
   rather than skip. Live feed always carries `id` (21/21); USGS guarantees it. Acceptable as-is;
   noting for the watch-list only.

## Defects
None.
