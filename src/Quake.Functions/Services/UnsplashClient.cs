using System.Text.Json;
using Quake.Core.Abstractions;
using Quake.Core.Models;

namespace Quake.Functions.Services;

public sealed class UnsplashClient(HttpClient http) : IPhotoClient
{
    // Authorization: Client-ID <key> header is configured on the HttpClient in Program.cs.
    // Demo tier = 50 req/hr; 3 photos/card stays well within budget.
    public async Task<IReadOnlyList<PhotoInfo>> SearchAsync(string query, int count = 3, CancellationToken ct = default)
    {
        using var resp = await http.GetAsync(
            $"search/photos?query={Uri.EscapeDataString(query)}&per_page={count}&orientation=landscape", ct);
        if (!resp.IsSuccessStatusCode) return [];
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("results", out var results)) return [];

        var list = new List<PhotoInfo>();
        foreach (var item in results.EnumerateArray())
        {
            var urls = item.TryGetProperty("urls", out var u) ? u : default;
            var user = item.TryGetProperty("user", out var us) ? us : default;
            var links = user.ValueKind == JsonValueKind.Object && user.TryGetProperty("links", out var l)
                ? l : default;

            list.Add(new PhotoInfo(
                ImageUrl: Str(urls, "regular"),
                ThumbUrl: Str(urls, "small"),
                PhotographerName: Str(user, "name"),
                PhotographerUrl: Str(links, "html")));
        }
        return list;
    }

    private static string Str(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v)
            ? v.GetString() ?? "" : "";
}
