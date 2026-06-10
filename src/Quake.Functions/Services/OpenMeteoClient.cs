using System.Text.Json;
using Quake.Core.Abstractions;
using Quake.Core.Models;

namespace Quake.Functions.Services;

public sealed class OpenMeteoClient(HttpClient http) : IWeatherClient
{
    // WMO weather interpretation codes (Open-Meteo `weather_code`).
    private static readonly Dictionary<int, string> WmoCodes = new()
    {
        [0] = "Clear sky", [1] = "Mainly clear", [2] = "Partly cloudy", [3] = "Overcast",
        [45] = "Fog", [48] = "Depositing rime fog", [51] = "Light drizzle", [53] = "Drizzle",
        [55] = "Dense drizzle", [61] = "Light rain", [63] = "Rain", [65] = "Heavy rain",
        [71] = "Light snow", [73] = "Snow", [75] = "Heavy snow", [80] = "Rain showers",
        [81] = "Heavy rain showers", [82] = "Violent rain showers", [95] = "Thunderstorm",
        [96] = "Thunderstorm with hail", [99] = "Thunderstorm with heavy hail",
    };

    public async Task<WeatherSnapshot?> GetCurrentAsync(double lat, double lon, CancellationToken ct = default)
    {
        // `current=` params are comma-joined; the response `current` object mirrors them.
        var url = $"v1/forecast?latitude={lat:F4}&longitude={lon:F4}" +
                  "&current=temperature_2m,wind_speed_10m,weather_code";
        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("current", out var cur)) return null;

        var code = cur.TryGetProperty("weather_code", out var wc) ? wc.GetInt32() : -1;
        return new WeatherSnapshot(
            TemperatureC: cur.TryGetProperty("temperature_2m", out var temp) ? temp.GetDouble() : 0,
            WindSpeedKmh: cur.TryGetProperty("wind_speed_10m", out var wind) ? wind.GetDouble() : 0,
            WeatherCode: code,
            Description: WmoCodes.GetValueOrDefault(code, "Unknown"));
    }
}
