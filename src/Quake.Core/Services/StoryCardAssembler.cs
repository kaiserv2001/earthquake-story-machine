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
        var photosTask = Safe<IReadOnlyList<PhotoInfo>>(async () => await photos.SearchAsync(subject, 3, ct), "photos");
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
