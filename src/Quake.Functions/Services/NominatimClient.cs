using System.Text.Json;
using Quake.Core.Abstractions;
using Quake.Core.Models;

namespace Quake.Functions.Services;

public sealed class NominatimClient(HttpClient http) : IGeocodingClient
{
    // Nominatim usage policy: max 1 req/sec, identifying User-Agent (both set in Program.cs DI).
    public async Task<LocationInfo?> ReverseGeocodeAsync(double lat, double lon, CancellationToken ct = default)
    {
        var url = $"reverse?lat={lat:F4}&lon={lon:F4}&format=jsonv2&zoom=10&accept-language=en";
        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

        // Ocean epicenters return {"error":"Unable to geocode"} — no address at all. That is the
        // common case for quakes, so a missing address is a clean null, not a failure.
        if (!doc.RootElement.TryGetProperty("address", out var addr)) return null;

        string? Get(params string[] keys)
        {
            foreach (var k in keys)
                if (addr.TryGetProperty(k, out var v)) return v.GetString();
            return null;
        }

        var display = doc.RootElement.TryGetProperty("display_name", out var dn)
            ? dn.GetString() ?? "" : "";

        return new LocationInfo(
            City: Get("city", "town", "village", "municipality", "county"),
            Region: Get("state", "province", "region"),
            Country: Get("country"),
            DisplayName: display);
    }
}
