using System.Text.Json;
using Quake.Core.Models;

namespace Quake.Core.Services;

public static class UsgsFeedParser
{
    public static IReadOnlyList<QuakeEvent> Parse(string geoJson, double minMagnitude = 4.5)
    {
        using var doc = JsonDocument.Parse(geoJson);
        var results = new List<QuakeEvent>();
        foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var props = feature.GetProperty("properties");
            var mag = props.GetProperty("mag").ValueKind == JsonValueKind.Null
                ? 0 : props.GetProperty("mag").GetDouble();
            if (mag < minMagnitude) continue;

            var coords = feature.GetProperty("geometry").GetProperty("coordinates");
            results.Add(new QuakeEvent
            {
                Id = feature.GetProperty("id").GetString()!,
                Magnitude = mag,
                Place = props.GetProperty("place").GetString() ?? "Unknown location",
                Longitude = coords[0].GetDouble(),
                Latitude = coords[1].GetDouble(),
                DepthKm = coords[2].GetDouble(),
                OccurredUtc = DateTimeOffset.FromUnixTimeMilliseconds(props.GetProperty("time").GetInt64()),
                Url = props.TryGetProperty("url", out var u) ? u.GetString() : null,
            });
        }
        return results;
    }
}
