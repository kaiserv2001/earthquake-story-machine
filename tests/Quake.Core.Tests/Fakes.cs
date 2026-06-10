using Quake.Core.Abstractions;
using Quake.Core.Models;

namespace Quake.Core.Tests;

/// <summary>Configurable test doubles for the enrichment interfaces.
/// Each records the argument it was called with so tests can assert the
/// subject the assembler chose (city → region → Place fallback).</summary>
internal sealed class FakeGeocoder : IGeocodingClient
{
    public Func<double, double, LocationInfo?> Behavior { get; init; } = (_, _) => null;
    public bool Throw { get; init; }

    public Task<LocationInfo?> ReverseGeocodeAsync(double lat, double lon, CancellationToken ct = default)
    {
        if (Throw) throw new InvalidOperationException("geocoder down");
        return Task.FromResult(Behavior(lat, lon));
    }
}

internal sealed class FakeWiki : IWikiClient
{
    public bool Throw { get; init; }
    public WikiSummary? Result { get; init; }
    public string? LastTitle { get; private set; }

    public Task<WikiSummary?> GetSummaryAsync(string title, CancellationToken ct = default)
    {
        LastTitle = title;
        if (Throw) throw new InvalidOperationException("wiki down");
        return Task.FromResult(Result);
    }
}

internal sealed class FakeWeather : IWeatherClient
{
    public bool Throw { get; init; }
    public WeatherSnapshot? Result { get; init; }

    public Task<WeatherSnapshot?> GetCurrentAsync(double lat, double lon, CancellationToken ct = default)
    {
        if (Throw) throw new InvalidOperationException("weather down");
        return Task.FromResult(Result);
    }
}

internal sealed class FakePhotos : IPhotoClient
{
    public bool Throw { get; init; }
    public IReadOnlyList<PhotoInfo> Result { get; init; } = [];
    public string? LastQuery { get; private set; }

    public Task<IReadOnlyList<PhotoInfo>> SearchAsync(string query, int count = 3, CancellationToken ct = default)
    {
        LastQuery = query;
        if (Throw) throw new InvalidOperationException("photos down");
        return Task.FromResult(Result);
    }
}

internal sealed class FakeHistory : IQuakeHistoryClient
{
    public bool Throw { get; init; }
    public HistoricalContext? Result { get; init; }

    public Task<HistoricalContext?> GetHistoryAsync(double lat, double lon, DateTimeOffset before, CancellationToken ct = default)
    {
        if (Throw) throw new InvalidOperationException("history down");
        return Task.FromResult(Result);
    }
}
