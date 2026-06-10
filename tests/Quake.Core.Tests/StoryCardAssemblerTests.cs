using Microsoft.Extensions.Logging.Abstractions;
using Quake.Core.Models;
using Quake.Core.Services;
using Xunit;

namespace Quake.Core.Tests;

public class StoryCardAssemblerTests
{
    private static QuakeEvent SampleQuake() => new()
    {
        Id = "us7000test",
        Magnitude = 6.1,
        Place = "120 km W of Sample Town",
        Latitude = 34.05,
        Longitude = -118.25,
        DepthKm = 12.3,
        OccurredUtc = DateTimeOffset.Parse("2026-06-10T08:00:00Z"),
        Url = "https://example.test/quake",
    };

    private static StoryCardAssembler Build(
        FakeGeocoder geo, FakeWiki wiki, FakeWeather weather, FakePhotos photos, FakeHistory history) =>
        new(geo, wiki, weather, photos, history, NullLogger<StoryCardAssembler>.Instance);

    [Fact]
    public async Task All_clients_succeed_produces_full_card()
    {
        var geo = new FakeGeocoder { Behavior = (_, _) => new LocationInfo("Sample City", "Sample State", "Testland", "Sample City, Testland") };
        var wiki = new FakeWiki { Result = new WikiSummary("Sample City", "A city.", "https://wiki/test", null) };
        var weather = new FakeWeather { Result = new WeatherSnapshot(21.0, 8.0, 1, "Mainly clear") };
        var photos = new FakePhotos { Result = [new PhotoInfo("img", "thumb", "Photographer", "https://p/test")] };
        var history = new FakeHistory { Result = new HistoricalContext(3, 5.4) };
        var assembler = Build(geo, wiki, weather, photos, history);

        var card = await assembler.AssembleAsync(SampleQuake());

        Assert.Equal("us7000test", card.Quake.Id);
        Assert.NotNull(card.Location);
        Assert.NotNull(card.Wiki);
        Assert.NotNull(card.Weather);
        Assert.Single(card.Photos);
        Assert.NotNull(card.History);
        Assert.NotEqual(default, card.GeneratedUtc);
        // Geocoder resolved a city, so the wiki/photo subject is that city.
        Assert.Equal("Sample City", wiki.LastTitle);
        Assert.Equal("Sample City", photos.LastQuery);
    }

    [Fact]
    public async Task Wiki_client_throws_card_still_produced_with_null_wiki()
    {
        var geo = new FakeGeocoder { Behavior = (_, _) => new LocationInfo("Sample City", "Sample State", "Testland", "Sample City") };
        var wiki = new FakeWiki { Throw = true };
        var weather = new FakeWeather { Result = new WeatherSnapshot(21.0, 8.0, 1, "Mainly clear") };
        var photos = new FakePhotos { Result = [] };
        var history = new FakeHistory { Result = new HistoricalContext(0, null) };
        var assembler = Build(geo, wiki, weather, photos, history);

        var card = await assembler.AssembleAsync(SampleQuake());

        Assert.Null(card.Wiki);                 // dead API → null section, not a thrown exception
        Assert.NotNull(card.Location);
        Assert.NotNull(card.Weather);           // other sections unaffected
        Assert.NotNull(card.History);
    }

    [Fact]
    public async Task Geocoder_null_falls_back_to_quake_place_for_queries()
    {
        var geo = new FakeGeocoder { Behavior = (_, _) => null };  // geocoding produced nothing
        var wiki = new FakeWiki { Result = null };
        var weather = new FakeWeather { Result = null };
        var photos = new FakePhotos { Result = [] };
        var history = new FakeHistory { Result = null };
        var assembler = Build(geo, wiki, weather, photos, history);

        var quake = SampleQuake();
        var card = await assembler.AssembleAsync(quake);

        Assert.Null(card.Location);
        // No city/region available → wiki and photos are queried with the USGS Place label.
        Assert.Equal(quake.Place, wiki.LastTitle);
        Assert.Equal(quake.Place, photos.LastQuery);
        Assert.Empty(card.Photos);
    }

    [Fact]
    public async Task Geocoder_throws_card_still_produced_with_null_location()
    {
        var geo = new FakeGeocoder { Throw = true };
        var wiki = new FakeWiki { Result = null };
        var weather = new FakeWeather { Result = null };
        var photos = new FakePhotos { Result = [] };
        var history = new FakeHistory { Result = null };
        var assembler = Build(geo, wiki, weather, photos, history);

        var quake = SampleQuake();
        var card = await assembler.AssembleAsync(quake);

        Assert.Null(card.Location);
        // A thrown geocoder must still fall back to Place, never crash the card.
        Assert.Equal(quake.Place, wiki.LastTitle);
    }
}
