using Quake.Core.Services;
using Xunit;

namespace Quake.Core.Tests;

public class UsgsFeedParserTests
{
    // Faithful to the real USGS 4.5_day.geojson summary feed shape (captured 2026-06-10).
    // Three features exercise the filter: M5.1 (kept), M4.2 (below default 4.5, dropped),
    // and a null-mag feature (dropped without throwing).
    private const string Feed = """
    {
      "type": "FeatureCollection",
      "metadata": { "generated": 1781087511000, "title": "USGS Magnitude 4.5+ Earthquakes, Past Day", "status": 200, "count": 3 },
      "features": [
        {
          "type": "Feature",
          "properties": {
            "mag": 5.1,
            "place": "7 km NW of Nuing, Philippines",
            "time": 1781082822703,
            "updated": 1781083000000,
            "url": "https://earthquake.usgs.gov/earthquakes/eventpage/us7000srzt",
            "type": "earthquake"
          },
          "geometry": { "type": "Point", "coordinates": [125.3874, 5.6757, 61.705] },
          "id": "us7000srzt"
        },
        {
          "type": "Feature",
          "properties": {
            "mag": 4.2,
            "place": "south of the Fiji Islands",
            "time": 1781080000000,
            "updated": 1781080100000,
            "url": "https://earthquake.usgs.gov/earthquakes/eventpage/us7000below",
            "type": "earthquake"
          },
          "geometry": { "type": "Point", "coordinates": [-178.1, -23.4, 540.0] },
          "id": "us7000below"
        },
        {
          "type": "Feature",
          "properties": {
            "mag": null,
            "place": "unknown event with no magnitude",
            "time": 1781070000000,
            "updated": 1781070100000,
            "type": "earthquake"
          },
          "geometry": { "type": "Point", "coordinates": [10.0, 20.0, 5.0] },
          "id": "us7000nullmag"
        }
      ],
      "bbox": [-178.1, -23.4, 5.0, 125.3874, 5.6757, 540.0]
    }
    """;

    [Fact]
    public void Parses_only_quakes_at_or_above_min_magnitude()
    {
        var result = UsgsFeedParser.Parse(Feed, minMagnitude: 4.5);

        var q = Assert.Single(result);            // only the M5.1 survives the default filter
        Assert.Equal("us7000srzt", q.Id);
        Assert.Equal(5.1, q.Magnitude);
    }

    [Fact]
    public void Maps_all_fields_including_lon_lat_depth_and_time()
    {
        var q = Assert.Single(UsgsFeedParser.Parse(Feed, minMagnitude: 4.5));

        Assert.Equal("7 km NW of Nuing, Philippines", q.Place);
        Assert.Equal(125.3874, q.Longitude);      // coordinates[0]
        Assert.Equal(5.6757, q.Latitude);         // coordinates[1]
        Assert.Equal(61.705, q.DepthKm);          // coordinates[2]
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1781082822703), q.OccurredUtc);
        Assert.Equal("https://earthquake.usgs.gov/earthquakes/eventpage/us7000srzt", q.Url);
    }

    [Fact]
    public void Null_magnitude_is_treated_as_zero_and_excluded_by_any_positive_threshold()
    {
        // A null mag must not crash parsing; it is treated as magnitude 0, so every
        // realistic threshold (the default 4.5, or any value > 0) excludes it.
        var atDefault = UsgsFeedParser.Parse(Feed, minMagnitude: 4.5);
        var atTiny = UsgsFeedParser.Parse(Feed, minMagnitude: 0.1);

        Assert.DoesNotContain(atDefault, q => q.Id == "us7000nullmag");
        Assert.DoesNotContain(atTiny, q => q.Id == "us7000nullmag");
    }

    [Fact]
    public void Lower_threshold_includes_more_quakes()
    {
        var result = UsgsFeedParser.Parse(Feed, minMagnitude: 4.0);

        Assert.Equal(2, result.Count);            // M5.1 and M4.2, still excluding null-mag
        Assert.Contains(result, q => q.Id == "us7000below");
    }

    [Fact]
    public void Empty_feature_collection_returns_empty()
    {
        const string empty = """{ "type": "FeatureCollection", "features": [] }""";

        Assert.Empty(UsgsFeedParser.Parse(empty));
    }
}
