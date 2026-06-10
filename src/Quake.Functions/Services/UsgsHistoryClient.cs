using System.Globalization;
using System.Text.Json;
using Quake.Core.Abstractions;
using Quake.Core.Models;

namespace Quake.Functions.Services;

public sealed class UsgsHistoryClient(HttpClient http) : IQuakeHistoryClient
{
    public async Task<HistoricalContext?> GetHistoryAsync(
        double lat, double lon, DateTimeOffset before, CancellationToken ct = default)
    {
        // FDSN `count` endpoint is cheap — quakes within 300 km over the trailing 30 days.
        var month = $"fdsnws/event/1/count?format=geojson&latitude={lat:F4}&longitude={lon:F4}" +
                    $"&maxradiuskm=300&starttime={Day(before.AddDays(-30))}&endtime={Day(before)}";
        // Max magnitude over the trailing year: order by magnitude, take the top one.
        var year = $"fdsnws/event/1/query?format=geojson&latitude={lat:F4}&longitude={lon:F4}" +
                   $"&maxradiuskm=300&starttime={Day(before.AddYears(-1))}&endtime={Day(before)}" +
                   "&orderby=magnitude&limit=1";

        using var countResp = await http.GetAsync(month, ct);
        if (!countResp.IsSuccessStatusCode) return null;
        using var countDoc = JsonDocument.Parse(await countResp.Content.ReadAsStringAsync(ct));
        var count = countDoc.RootElement.TryGetProperty("count", out var c) ? c.GetInt32() : 0;

        double? maxMag = null;
        using var maxResp = await http.GetAsync(year, ct);
        if (maxResp.IsSuccessStatusCode)
        {
            using var maxDoc = JsonDocument.Parse(await maxResp.Content.ReadAsStringAsync(ct));
            if (maxDoc.RootElement.TryGetProperty("features", out var features)
                && features.GetArrayLength() > 0
                && features[0].TryGetProperty("properties", out var props)
                && props.TryGetProperty("mag", out var magEl)
                && magEl.ValueKind == JsonValueKind.Number)
            {
                maxMag = magEl.GetDouble();
            }
        }
        return new HistoricalContext(count, maxMag);
    }

    private static string Day(DateTimeOffset d) =>
        d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
