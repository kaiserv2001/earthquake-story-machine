using System.Text.Json;
using Quake.Core.Abstractions;
using Quake.Core.Models;

namespace Quake.Functions.Services;

public sealed class WikipediaClient(HttpClient http) : IWikiClient
{
    public async Task<WikiSummary?> GetSummaryAsync(string title, CancellationToken ct = default)
    {
        using var resp = await http.GetAsync(
            $"api/rest_v1/page/summary/{Uri.EscapeDataString(title)}?redirect=true", ct);
        if (!resp.IsSuccessStatusCode) return null;   // 404 on no such page
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        // A disambiguation page is not a real location summary — treat it as a miss.
        if (root.TryGetProperty("type", out var t) && t.GetString() == "disambiguation") return null;

        return new WikiSummary(
            Title: root.TryGetProperty("title", out var ti) ? ti.GetString() ?? title : title,
            Extract: root.TryGetProperty("extract", out var e) ? e.GetString() ?? "" : "",
            PageUrl: root.TryGetProperty("content_urls", out var cu)
                     && cu.TryGetProperty("desktop", out var desk)
                     && desk.TryGetProperty("page", out var page)
                ? page.GetString() : null,
            ThumbnailUrl: root.TryGetProperty("thumbnail", out var th)
                          && th.TryGetProperty("source", out var src)
                ? src.GetString() : null);
    }
}
