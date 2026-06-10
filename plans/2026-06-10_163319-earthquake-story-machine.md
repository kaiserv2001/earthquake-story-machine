# Earthquake Story Machine — Implementation Plan

**Goal:** Event-driven Azure pipeline that turns every significant earthquake (USGS feed) into a rich, browsable "story card" enriched with geocoding, Wikipedia context, weather, and photos.

**Architecture:** A timer-triggered Azure Function polls the USGS GeoJSON feed and publishes new quakes to an Azure Service Bus queue. A Service-Bus-triggered story-builder Function fans out to five enrichment APIs in parallel, assembles a story card, writes the card JSON to Blob Storage, and records metadata in Azure SQL. An HTTP-triggered Function exposes the cards to a Static Web App frontend.

**Tech Stack:** .NET 8 (isolated worker), Azure Functions v4, Azure Service Bus, Azure Blob Storage, Azure SQL + EF Core 8, Azure Static Web Apps (vanilla HTML/JS frontend), Bicep (infra), xUnit (tests).

**External APIs (all free):**
| API | Purpose | Auth |
|---|---|---|
| USGS Earthquake GeoJSON feed | Trigger source | none |
| Nominatim (OpenStreetMap) | Reverse geocoding lat/lon → city/region | none (User-Agent header required) |
| Wikipedia REST API | Location summary | none |
| Open-Meteo | Weather at epicenter | none |
| Unsplash API | Photos of nearest city | free access key |

**Repository layout (target):**
```
/mnt/d/Code/Earthquake/
├── EarthquakeStoryMachine.sln
├── src/
│   ├── Quake.Core/          # domain models, interfaces, assembler (pure, testable)
│   ├── Quake.Data/          # EF Core DbContext + entities
│   └── Quake.Functions/     # Azure Functions host (poller, builder, API)
├── frontend/                # Static Web App
├── tests/Quake.Core.Tests/
├── infra/                   # Bicep templates
├── plans/
└── docs/sprints/
```

---

## Phase 1 — Solution skeleton

### Task 1 — Create solution and projects

**Objective:** Compiling empty solution with all four projects wired together.

**Files:**
- Create: `EarthquakeStoryMachine.sln`, `src/Quake.Core/Quake.Core.csproj`, `src/Quake.Data/Quake.Data.csproj`, `src/Quake.Functions/Quake.Functions.csproj`, `tests/Quake.Core.Tests/Quake.Core.Tests.csproj`

**Steps:**
1. `dotnet new sln -n EarthquakeStoryMachine`
2. `dotnet new classlib -n Quake.Core -o src/Quake.Core -f net8.0`
3. `dotnet new classlib -n Quake.Data -o src/Quake.Data -f net8.0`
4. `dotnet new func --worker-runtime dotnet-isolated -n Quake.Functions -o src/Quake.Functions` (or create csproj manually from code below if `func` templates unavailable)
5. `dotnet new xunit -n Quake.Core.Tests -o tests/Quake.Core.Tests -f net8.0`
6. `dotnet sln add src/**/ *.csproj tests/**/*.csproj` (add each project)
7. References: Functions → Core + Data; Data → Core; Tests → Core
8. Verify: `dotnet build` → `Build succeeded. 0 Warning(s). 0 Error(s).`

**Code — `src/Quake.Functions/Quake.Functions.csproj`:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AzureFunctionsVersion>v4</AzureFunctionsVersion>
    <OutputType>Exe</OutputType>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Azure.Functions.Worker" Version="1.23.0" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="1.17.0" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Http" Version="3.2.0" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore" Version="1.3.0" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Timer" Version="4.3.0" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.ServiceBus" Version="5.16.0" />
    <PackageReference Include="Azure.Storage.Blobs" Version="12.21.0" />
    <PackageReference Include="Microsoft.ApplicationInsights.WorkerService" Version="2.22.0" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.ApplicationInsights" Version="1.2.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Quake.Core\Quake.Core.csproj" />
    <ProjectReference Include="..\Quake.Data\Quake.Data.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Update="host.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>
    <None Update="local.settings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>Never</CopyToPublishDirectory>
    </None>
  </ItemGroup>
</Project>
```

---

### Task 2 — Domain models in Quake.Core

**Objective:** All pipeline message and card shapes exist as records.

**Files:**
- Create: `src/Quake.Core/Models/QuakeEvent.cs`, `src/Quake.Core/Models/StoryCard.cs`

**Code — `src/Quake.Core/Models/QuakeEvent.cs`:**
```csharp
namespace Quake.Core.Models;

/// <summary>Message published to Service Bus per detected quake.</summary>
public sealed record QuakeEvent
{
    public required string Id { get; init; }            // USGS event id, e.g. "us7000abcd"
    public required double Magnitude { get; init; }
    public required string Place { get; init; }         // USGS human label
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required double DepthKm { get; init; }
    public required DateTimeOffset OccurredUtc { get; init; }
    public string? Url { get; init; }                   // USGS event page
}
```

**Code — `src/Quake.Core/Models/StoryCard.cs`:**
```csharp
namespace Quake.Core.Models;

public sealed record LocationInfo(string? City, string? Region, string? Country, string DisplayName);
public sealed record WikiSummary(string Title, string Extract, string? PageUrl, string? ThumbnailUrl);
public sealed record WeatherSnapshot(double TemperatureC, double WindSpeedKmh, int WeatherCode, string Description);
public sealed record PhotoInfo(string ImageUrl, string ThumbUrl, string PhotographerName, string PhotographerUrl);
public sealed record HistoricalContext(int QuakesLast30DaysWithin300Km, double? MaxMagnitudeLastYear);

/// <summary>The assembled artifact stored in Blob and surfaced by the API.</summary>
public sealed record StoryCard
{
    public required QuakeEvent Quake { get; init; }
    public LocationInfo? Location { get; init; }
    public WikiSummary? Wiki { get; init; }
    public WeatherSnapshot? Weather { get; init; }
    public IReadOnlyList<PhotoInfo> Photos { get; init; } = [];
    public HistoricalContext? History { get; init; }
    public required DateTimeOffset GeneratedUtc { get; init; }
}
```

**Verify:** `dotnet build src/Quake.Core` passes.

---

### Task 3 — Enrichment interfaces in Quake.Core

**Objective:** Every enrichment step has an interface so the assembler and tests never touch HTTP.

**Files:**
- Create: `src/Quake.Core/Abstractions/IEnrichmentClients.cs`

**Code:**
```csharp
using Quake.Core.Models;

namespace Quake.Core.Abstractions;

public interface IGeocodingClient
{
    Task<LocationInfo?> ReverseGeocodeAsync(double lat, double lon, CancellationToken ct = default);
}

public interface IWikiClient
{
    Task<WikiSummary?> GetSummaryAsync(string title, CancellationToken ct = default);
}

public interface IWeatherClient
{
    Task<WeatherSnapshot?> GetCurrentAsync(double lat, double lon, CancellationToken ct = default);
}

public interface IPhotoClient
{
    Task<IReadOnlyList<PhotoInfo>> SearchAsync(string query, int count = 3, CancellationToken ct = default);
}

public interface IQuakeHistoryClient
{
    Task<HistoricalContext?> GetHistoryAsync(double lat, double lon, DateTimeOffset before, CancellationToken ct = default);
}

public interface IStoryCardStore
{
    /// <returns>Blob path of the stored card.</returns>
    Task<string> SaveAsync(StoryCard card, CancellationToken ct = default);
    Task<StoryCard?> GetAsync(string quakeId, CancellationToken ct = default);
}
```

---

## Phase 2 — Core pipeline logic (TDD)

### Task 4 — StoryCardAssembler with tests

**Objective:** Pure assembler that fans out to all enrichment clients in parallel and tolerates individual failures (a dead API never kills the card).

**Files:**
- Create: `src/Quake.Core/Services/StoryCardAssembler.cs`
- Test: `tests/Quake.Core.Tests/StoryCardAssemblerTests.cs`

**Steps:**
1. Write tests first: (a) all clients succeed → full card; (b) wiki client throws → card still produced with `Wiki == null`; (c) geocoder returns null → photo/wiki queries fall back to `QuakeEvent.Place`.
2. `dotnet test` → fails (red).
3. Implement assembler. 4. `dotnet test` → green. 5. Commit `feat: story card assembler`.

**Code — `src/Quake.Core/Services/StoryCardAssembler.cs`:**
```csharp
using Microsoft.Extensions.Logging;
using Quake.Core.Abstractions;
using Quake.Core.Models;

namespace Quake.Core.Services;

public sealed class StoryCardAssembler(
    IGeocodingClient geocoder,
    IWikiClient wiki,
    IWeatherClient weather,
    IPhotoClient photos,
    IQuakeHistoryClient history,
    ILogger<StoryCardAssembler> logger)
{
    public async Task<StoryCard> AssembleAsync(QuakeEvent quake, CancellationToken ct = default)
    {
        var location = await Safe(() => geocoder.ReverseGeocodeAsync(quake.Latitude, quake.Longitude, ct), "geocoding");
        var subject = location?.City ?? location?.Region ?? quake.Place;

        var wikiTask = Safe(() => wiki.GetSummaryAsync(subject, ct), "wikipedia");
        var weatherTask = Safe(() => weather.GetCurrentAsync(quake.Latitude, quake.Longitude, ct), "weather");
        var photosTask = Safe(() => photos.SearchAsync(subject, 3, ct), "photos");
        var historyTask = Safe(() => history.GetHistoryAsync(quake.Latitude, quake.Longitude, quake.OccurredUtc, ct), "history");
        await Task.WhenAll(wikiTask, weatherTask, photosTask, historyTask);

        return new StoryCard
        {
            Quake = quake,
            Location = location,
            Wiki = wikiTask.Result,
            Weather = weatherTask.Result,
            Photos = photosTask.Result ?? [],
            History = historyTask.Result,
            GeneratedUtc = DateTimeOffset.UtcNow,
        };
    }

    private async Task<T?> Safe<T>(Func<Task<T?>> step, string name)
    {
        try { return await step(); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Enrichment step {Step} failed; continuing without it", name);
            return default;
        }
    }
}
```

---

### Task 5 — USGS feed parser with tests

**Objective:** Parse the USGS GeoJSON summary feed into `QuakeEvent` records, filtering by minimum magnitude.

**Files:**
- Create: `src/Quake.Core/Services/UsgsFeedParser.cs`
- Test: `tests/Quake.Core.Tests/UsgsFeedParserTests.cs` (embed a real captured feed sample as a test fixture string)

**Code — `src/Quake.Core/Services/UsgsFeedParser.cs`:**
```csharp
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
```

---

## Phase 3 — Enrichment HTTP clients (Quake.Functions/Services)

All five clients follow the same shape: typed `HttpClient` via `IHttpClientFactory`, JSON parse with `System.Text.Json`, return `null` on non-success. One task per client; each is independent and parallelizable across agents.

### Task 6 — NominatimClient (`IGeocodingClient`)

**Files:** Create: `src/Quake.Functions/Services/NominatimClient.cs`

**Code:**
```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Quake.Core.Abstractions;
using Quake.Core.Models;

namespace Quake.Functions.Services;

public sealed class NominatimClient(HttpClient http) : IGeocodingClient
{
    // Nominatim usage policy: max 1 req/sec, User-Agent identifying the app (set in DI).
    public async Task<LocationInfo?> ReverseGeocodeAsync(double lat, double lon, CancellationToken ct = default)
    {
        var url = $"reverse?lat={lat:F4}&lon={lon:F4}&format=jsonv2&zoom=10&accept-language=en";
        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("address", out var addr)) return null;

        string? Get(params string[] keys)
        {
            foreach (var k in keys)
                if (addr.TryGetProperty(k, out var v)) return v.GetString();
            return null;
        }
        return new LocationInfo(
            City: Get("city", "town", "village", "municipality", "county"),
            Region: Get("state", "province", "region"),
            Country: Get("country"),
            DisplayName: doc.RootElement.GetProperty("display_name").GetString() ?? "");
    }
}
```

### Task 7 — WikipediaClient (`IWikiClient`)

**Files:** Create: `src/Quake.Functions/Services/WikipediaClient.cs`

**Code:**
```csharp
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
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        if (root.TryGetProperty("type", out var t) && t.GetString() == "disambiguation") return null;
        return new WikiSummary(
            Title: root.GetProperty("title").GetString() ?? title,
            Extract: root.TryGetProperty("extract", out var e) ? e.GetString() ?? "" : "",
            PageUrl: root.TryGetProperty("content_urls", out var cu)
                ? cu.GetProperty("desktop").GetProperty("page").GetString() : null,
            ThumbnailUrl: root.TryGetProperty("thumbnail", out var th)
                ? th.GetProperty("source").GetString() : null);
    }
}
```

### Task 8 — OpenMeteoClient (`IWeatherClient`)

**Files:** Create: `src/Quake.Functions/Services/OpenMeteoClient.cs`

**Code:**
```csharp
using System.Text.Json;
using Quake.Core.Abstractions;
using Quake.Core.Models;

namespace Quake.Functions.Services;

public sealed class OpenMeteoClient(HttpClient http) : IWeatherClient
{
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
        var url = $"v1/forecast?latitude={lat:F4}&longitude={lon:F4}" +
                  "&current=temperature_2m,wind_speed_10m,weather_code";
        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var cur = doc.RootElement.GetProperty("current");
        var code = cur.GetProperty("weather_code").GetInt32();
        return new WeatherSnapshot(
            TemperatureC: cur.GetProperty("temperature_2m").GetDouble(),
            WindSpeedKmh: cur.GetProperty("wind_speed_10m").GetDouble(),
            WeatherCode: code,
            Description: WmoCodes.GetValueOrDefault(code, "Unknown"));
    }
}
```

### Task 9 — UnsplashClient (`IPhotoClient`)

**Files:** Create: `src/Quake.Functions/Services/UnsplashClient.cs`

**Code:**
```csharp
using System.Text.Json;
using Quake.Core.Abstractions;
using Quake.Core.Models;

namespace Quake.Functions.Services;

public sealed class UnsplashClient(HttpClient http) : IPhotoClient
{
    // Authorization: Client-ID <key> header is configured on the HttpClient in Program.cs.
    public async Task<IReadOnlyList<PhotoInfo>> SearchAsync(string query, int count = 3, CancellationToken ct = default)
    {
        using var resp = await http.GetAsync(
            $"search/photos?query={Uri.EscapeDataString(query)}&per_page={count}&orientation=landscape", ct);
        if (!resp.IsSuccessStatusCode) return [];
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var list = new List<PhotoInfo>();
        foreach (var item in doc.RootElement.GetProperty("results").EnumerateArray())
        {
            var urls = item.GetProperty("urls");
            var user = item.GetProperty("user");
            list.Add(new PhotoInfo(
                ImageUrl: urls.GetProperty("regular").GetString() ?? "",
                ThumbUrl: urls.GetProperty("small").GetString() ?? "",
                PhotographerName: user.GetProperty("name").GetString() ?? "",
                PhotographerUrl: user.GetProperty("links").GetProperty("html").GetString() ?? ""));
        }
        return list;
    }
}
```

### Task 10 — UsgsHistoryClient (`IQuakeHistoryClient`)

**Files:** Create: `src/Quake.Functions/Services/UsgsHistoryClient.cs`

**Code:**
```csharp
using System.Text.Json;
using Quake.Core.Abstractions;
using Quake.Core.Models;

namespace Quake.Functions.Services;

public sealed class UsgsHistoryClient(HttpClient http) : IQuakeHistoryClient
{
    public async Task<HistoricalContext?> GetHistoryAsync(
        double lat, double lon, DateTimeOffset before, CancellationToken ct = default)
    {
        // USGS FDSN event query: count quakes within 300 km over the last 30 days / max mag last year.
        var month = $"fdsnws/event/1/count?format=geojson&latitude={lat:F4}&longitude={lon:F4}" +
                    $"&maxradiuskm=300&starttime={before.AddDays(-30):yyyy-MM-dd}&endtime={before:yyyy-MM-dd}";
        var year = $"fdsnws/event/1/query?format=geojson&latitude={lat:F4}&longitude={lon:F4}" +
                   $"&maxradiuskm=300&starttime={before.AddYears(-1):yyyy-MM-dd}&endtime={before:yyyy-MM-dd}" +
                   "&orderby=magnitude&limit=1";

        using var countResp = await http.GetAsync(month, ct);
        if (!countResp.IsSuccessStatusCode) return null;
        using var countDoc = JsonDocument.Parse(await countResp.Content.ReadAsStringAsync(ct));
        var count = countDoc.RootElement.GetProperty("count").GetInt32();

        double? maxMag = null;
        using var maxResp = await http.GetAsync(year, ct);
        if (maxResp.IsSuccessStatusCode)
        {
            using var maxDoc = JsonDocument.Parse(await maxResp.Content.ReadAsStringAsync(ct));
            var features = maxDoc.RootElement.GetProperty("features");
            if (features.GetArrayLength() > 0)
            {
                var magEl = features[0].GetProperty("properties").GetProperty("mag");
                if (magEl.ValueKind != JsonValueKind.Null) maxMag = magEl.GetDouble();
            }
        }
        return new HistoricalContext(count, maxMag);
    }
}
```

---

## Phase 4 — Persistence

### Task 11 — EF Core DbContext in Quake.Data

**Objective:** SQL metadata store with idempotent quake tracking (dedup across poller runs).

**Files:**
- Create: `src/Quake.Data/Quake.Data.csproj` packages, `src/Quake.Data/Entities/StoryCardRecord.cs`, `src/Quake.Data/QuakeDbContext.cs`

**Packages:** `Microsoft.EntityFrameworkCore.SqlServer` 8.x, `Microsoft.EntityFrameworkCore.Design` 8.x

**Code — `src/Quake.Data/Entities/StoryCardRecord.cs`:**
```csharp
namespace Quake.Data.Entities;

public class StoryCardRecord
{
    public int Id { get; set; }
    public required string QuakeId { get; set; }     // unique index — dedup key
    public double Magnitude { get; set; }
    public required string Place { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTimeOffset OccurredUtc { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public required string BlobPath { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}
```

**Code — `src/Quake.Data/QuakeDbContext.cs`:**
```csharp
using Microsoft.EntityFrameworkCore;
using Quake.Data.Entities;

namespace Quake.Data;

public class QuakeDbContext(DbContextOptions<QuakeDbContext> options) : DbContext(options)
{
    public DbSet<StoryCardRecord> StoryCards => Set<StoryCardRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<StoryCardRecord>(e =>
        {
            e.HasIndex(x => x.QuakeId).IsUnique();
            e.Property(x => x.QuakeId).HasMaxLength(64);
            e.Property(x => x.Place).HasMaxLength(256);
            e.Property(x => x.BlobPath).HasMaxLength(512);
        });
    }
}
```

**Steps:** add migration `dotnet ef migrations add Initial -p src/Quake.Data -s src/Quake.Functions`; verify with `dotnet ef migrations list`.

### Task 12 — BlobStoryCardStore (`IStoryCardStore`)

**Files:** Create: `src/Quake.Functions/Services/BlobStoryCardStore.cs`

**Code:**
```csharp
using System.Text.Json;
using Azure.Storage.Blobs;
using Quake.Core.Abstractions;
using Quake.Core.Models;

namespace Quake.Functions.Services;

public sealed class BlobStoryCardStore(BlobServiceClient blobService) : IStoryCardStore
{
    private const string Container = "story-cards";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<string> SaveAsync(StoryCard card, CancellationToken ct = default)
    {
        var container = blobService.GetBlobContainerClient(Container);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        var path = $"{card.Quake.OccurredUtc:yyyy/MM}/{card.Quake.Id}.json";
        await container.GetBlobClient(path)
            .UploadAsync(BinaryData.FromString(JsonSerializer.Serialize(card, Json)), overwrite: true, ct);
        return path;
    }

    public async Task<StoryCard?> GetAsync(string quakeId, CancellationToken ct = default)
    {
        var container = blobService.GetBlobContainerClient(Container);
        await foreach (var blob in container.GetBlobsAsync(prefix: null, cancellationToken: ct))
        {
            if (!blob.Name.EndsWith($"/{quakeId}.json", StringComparison.Ordinal)) continue;
            var content = await container.GetBlobClient(blob.Name).DownloadContentAsync(ct);
            return JsonSerializer.Deserialize<StoryCard>(content.Value.Content.ToString(), Json);
        }
        return null;
    }
}
```

---

## Phase 5 — Azure Functions

### Task 13 — Program.cs (DI composition root)

**Files:** Create: `src/Quake.Functions/Program.cs`, `src/Quake.Functions/host.json`, `src/Quake.Functions/local.settings.json`

**Code — `src/Quake.Functions/Program.cs`:**
```csharp
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quake.Core.Abstractions;
using Quake.Core.Services;
using Quake.Data;
using Quake.Functions.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((ctx, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        var cfg = ctx.Configuration;
        services.AddHttpClient<IGeocodingClient, NominatimClient>(c =>
        {
            c.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
            c.DefaultRequestHeaders.UserAgent.ParseAdd("EarthquakeStoryMachine/1.0 (portfolio project)");
        });
        services.AddHttpClient<IWikiClient, WikipediaClient>(c =>
            c.BaseAddress = new Uri("https://en.wikipedia.org/"));
        services.AddHttpClient<IWeatherClient, OpenMeteoClient>(c =>
            c.BaseAddress = new Uri("https://api.open-meteo.com/"));
        services.AddHttpClient<IPhotoClient, UnsplashClient>(c =>
        {
            c.BaseAddress = new Uri("https://api.unsplash.com/");
            c.DefaultRequestHeaders.Authorization =
                new("Client-ID", cfg["UnsplashAccessKey"]);
        });
        services.AddHttpClient<IQuakeHistoryClient, UsgsHistoryClient>(c =>
            c.BaseAddress = new Uri("https://earthquake.usgs.gov/"));
        services.AddHttpClient("usgs-feed", c =>
            c.BaseAddress = new Uri("https://earthquake.usgs.gov/"));

        services.AddSingleton(new BlobServiceClient(cfg["BlobStorageConnection"]));
        services.AddSingleton<IStoryCardStore, BlobStoryCardStore>();
        services.AddScoped<StoryCardAssembler>();
        services.AddDbContext<QuakeDbContext>(o => o.UseSqlServer(cfg["SqlConnection"]));
    })
    .Build();

host.Run();
```

**Code — `src/Quake.Functions/local.settings.json`:**
```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "ServiceBusConnection": "<service-bus-connection-string-or-emulator>",
    "BlobStorageConnection": "UseDevelopmentStorage=true",
    "SqlConnection": "Server=localhost,1433;Database=QuakeDb;User Id=sa;Password=<localDevPassword!1>;TrustServerCertificate=true",
    "UnsplashAccessKey": "<unsplash-access-key>",
    "UsgsMinMagnitude": "4.5",
    "UsgsPollSchedule": "0 */5 * * * *"
  }
}
```

### Task 14 — UsgsPollerFunction (Timer → Service Bus)

**Objective:** Every 5 minutes, fetch USGS feed, drop already-seen quakes (SQL lookup), publish the rest to the `quake-events` queue.

**Files:** Create: `src/Quake.Functions/Functions/UsgsPollerFunction.cs`

**Code:**
```csharp
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quake.Core.Services;
using Quake.Data;

namespace Quake.Functions.Functions;

public class UsgsPollerFunction(
    IHttpClientFactory httpFactory,
    QuakeDbContext db,
    ILogger<UsgsPollerFunction> logger)
{
    [Function(nameof(UsgsPollerFunction))]
    [ServiceBusOutput("quake-events", Connection = "ServiceBusConnection")]
    public async Task<string[]> Run(
        [TimerTrigger("%UsgsPollSchedule%")] TimerInfo timer,
        CancellationToken ct)
    {
        var http = httpFactory.CreateClient("usgs-feed");
        var feed = await http.GetStringAsync(
            "earthquakes/feed/v1.0/summary/4.5_day.geojson", ct);
        var quakes = UsgsFeedParser.Parse(feed, minMagnitude: 4.5);

        var ids = quakes.Select(q => q.Id).ToArray();
        var seen = await db.StoryCards.Where(s => ids.Contains(s.QuakeId))
            .Select(s => s.QuakeId).ToHashSetAsync(ct);
        var fresh = quakes.Where(q => !seen.Contains(q.Id)).ToArray();

        logger.LogInformation("USGS poll: {Total} quakes in feed, {New} new", quakes.Count, fresh.Length);
        return fresh.Select(q => JsonSerializer.Serialize(q)).ToArray();
    }
}
```

### Task 15 — StoryBuilderFunction (Service Bus → Blob + SQL)

**Files:** Create: `src/Quake.Functions/Functions/StoryBuilderFunction.cs`

**Code:**
```csharp
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Quake.Core.Abstractions;
using Quake.Core.Models;
using Quake.Core.Services;
using Quake.Data;
using Quake.Data.Entities;

namespace Quake.Functions.Functions;

public class StoryBuilderFunction(
    StoryCardAssembler assembler,
    IStoryCardStore store,
    QuakeDbContext db,
    ILogger<StoryBuilderFunction> logger)
{
    [Function(nameof(StoryBuilderFunction))]
    public async Task Run(
        [ServiceBusTrigger("quake-events", Connection = "ServiceBusConnection")] string message,
        CancellationToken ct)
    {
        var quake = JsonSerializer.Deserialize<QuakeEvent>(message)
            ?? throw new InvalidOperationException("Unparseable quake event message");

        if (await db.StoryCards.AnyAsync(s => s.QuakeId == quake.Id, ct))
        {
            logger.LogInformation("Quake {Id} already has a card; skipping", quake.Id);
            return;
        }

        var card = await assembler.AssembleAsync(quake, ct);
        var blobPath = await store.SaveAsync(card, ct);

        db.StoryCards.Add(new StoryCardRecord
        {
            QuakeId = quake.Id,
            Magnitude = quake.Magnitude,
            Place = quake.Place,
            Latitude = quake.Latitude,
            Longitude = quake.Longitude,
            OccurredUtc = quake.OccurredUtc,
            City = card.Location?.City,
            Country = card.Location?.Country,
            BlobPath = blobPath,
            CreatedUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Story card created for M{Mag} {Place} → {Blob}",
            quake.Magnitude, quake.Place, blobPath);
    }
}
```
(Add `using Microsoft.EntityFrameworkCore;` for `AnyAsync`.)

### Task 16 — StoryCardsApiFunction (HTTP API for frontend)

**Files:** Create: `src/Quake.Functions/Functions/StoryCardsApiFunction.cs`

**Code:**
```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Quake.Core.Abstractions;
using Quake.Data;

namespace Quake.Functions.Functions;

public class StoryCardsApiFunction(QuakeDbContext db, IStoryCardStore store)
{
    [Function("GetStoryCards")]
    public async Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cards")] HttpRequest req,
        CancellationToken ct)
    {
        var cards = await db.StoryCards
            .OrderByDescending(s => s.OccurredUtc)
            .Take(50)
            .Select(s => new { s.QuakeId, s.Magnitude, s.Place, s.City, s.Country, s.OccurredUtc })
            .ToListAsync(ct);
        return new OkObjectResult(cards);
    }

    [Function("GetStoryCard")]
    public async Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cards/{quakeId}")] HttpRequest req,
        string quakeId,
        CancellationToken ct)
    {
        var card = await store.GetAsync(quakeId, ct);
        return card is null ? new NotFoundResult() : new OkObjectResult(card);
    }
}
```
(Requires `Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore` package + `ConfigureFunctionsWebApplication()` — already in Task 13.)

---

## Phase 6 — Frontend (Static Web App)

### Task 17 — Story card browser UI

**Objective:** Single-page frontend: grid of recent quake cards; clicking one opens the full story card (photos, wiki extract, weather, history).

**Files:**
- Create: `frontend/index.html`, `frontend/app.js`, `frontend/styles.css`, `frontend/staticwebapp.config.json`

**Approach:** Vanilla HTML/JS fetching `/api/cards` and `/api/cards/{id}` (Static Web Apps linked-backend proxies `/api/*` to the Functions app). Dark seismic-themed design — magnitude badge color-coded (4.5–5.4 amber, 5.5–6.4 orange, 6.5+ red), card layout with Unsplash hero image, weather chip, wiki extract, "history" stat strip. Detailed markup left to the frontend agent; design tokens to be specified in sprint doc (consider `Popular Web Designs` / `Creative Design` vault skills).

**Code — `frontend/staticwebapp.config.json`:**
```json
{
  "navigationFallback": { "rewrite": "/index.html" },
  "routes": [{ "route": "/api/*", "allowedRoles": ["anonymous"] }]
}
```

---

## Phase 7 — Infrastructure & deployment

### Task 18 — Bicep templates

**Objective:** One `azd`-style deployable template set: Service Bus namespace + `quake-events` queue, Storage account (blobs), Function App (consumption, dotnet-isolated), Azure SQL (serverless, free tier), Static Web App, Application Insights.

**Files:**
- Create: `infra/main.bicep`, `infra/main.bicepparam`

**Key resources (names/SKUs):**
- `Microsoft.ServiceBus/namespaces` — Basic SKU (queues only, cheapest) + queue `quake-events` (maxDeliveryCount 5, lock 5 min)
- `Microsoft.Storage/storageAccounts` — Standard_LRS, container `story-cards`
- `Microsoft.Web/sites` (functionapp) — Y1 consumption, `FUNCTIONS_WORKER_RUNTIME=dotnet-isolated`, app settings for all connections
- `Microsoft.Sql/servers` + database — GP_S_Gen5_1 serverless w/ auto-pause (or Free offer)
- `Microsoft.Web/staticSites` — Free SKU
- `Microsoft.Insights/components`

**Verify:** `az bicep build --file infra/main.bicep` → no errors. Full Bicep authored by infra agent in Sprint 2.

### Task 19 — Local dev environment + run-through

**Objective:** End-to-end local run: Azurite + SQL in Docker + Service Bus emulator + `func start`.

**Files:** Create: `docker-compose.yml` (azurite, mssql, servicebus-emulator), `README.md` quick-start section

**Verify:** `func start` in `src/Quake.Functions` → poller fires → message hits emulator queue → story builder logs "Story card created" → `curl http://localhost:7071/api/cards` returns JSON.

### Task 20 — CI/CD (GitHub Actions)

**Objective:** Build + test on PR; deploy Functions and Static Web App on main.

**Files:** Create: `.github/workflows/ci.yml`, `.github/workflows/deploy.yml`

**Verify:** Green build on push.

---

## Review checklist

- [x] Tasks ordered: skeleton → core (TDD) → clients → persistence → functions → frontend → infra
- [x] No task depends on a later task
- [x] Phases 3 (clients), 6 (frontend), 7 (infra) are independent once Phases 1–2 land — parallelizable across agents
- [x] Every code task has exact paths and complete code
- [x] Failure isolation: one dead enrichment API degrades the card, never kills it
- [x] Idempotency: unique `QuakeId` index + dedup checks in both poller and builder

**Risks / notes:**
- Nominatim rate limit is 1 req/sec — fine at quake volumes, but keep the User-Agent header or requests get blocked.
- Unsplash demo tier = 50 req/hour — 3 photos/card is well within budget.
- Service Bus emulator requires Docker; if unavailable locally, point `ServiceBusConnection` at the real (Basic, ~free) namespace.
- `dotnet ef` migrations need a design-time factory or the Functions project as startup; if it fights, add a `DesignTimeDbContextFactory` in Quake.Data.
