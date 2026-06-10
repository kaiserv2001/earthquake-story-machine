using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Quake.Core;
using Quake.Core.Services;
using Quake.Data;

namespace Quake.Functions.Functions;

public class UsgsPollerFunction(
    IHttpClientFactory httpFactory,
    QuakeDbContext db,
    IConfiguration cfg,
    ILogger<UsgsPollerFunction> logger)
{
    [Function(nameof(UsgsPollerFunction))]
    [ServiceBusOutput("quake-events", Connection = "ServiceBusConnection")]
    public async Task<string[]> Run(
        [TimerTrigger("%UsgsPollSchedule%")] TimerInfo timer,
        CancellationToken ct)
    {
        var minMag = double.TryParse(cfg["UsgsMinMagnitude"], out var m) ? m : 4.5;

        var http = httpFactory.CreateClient("usgs-feed");
        var feed = await http.GetStringAsync(
            "earthquakes/feed/v1.0/summary/4.5_day.geojson", ct);
        var quakes = UsgsFeedParser.Parse(feed, minMagnitude: minMag);

        var ids = quakes.Select(q => q.Id).ToArray();
        // ToHashSetAsync lands in EF Core 9; on EF Core 8 we materialize then set-ify.
        var seen = (await db.StoryCards.Where(s => ids.Contains(s.QuakeId))
            .Select(s => s.QuakeId).ToListAsync(ct)).ToHashSet();
        var fresh = quakes.Where(q => !seen.Contains(q.Id)).ToArray();

        logger.LogInformation("USGS poll: {Total} quakes in feed, {New} new", quakes.Count, fresh.Length);

        // Serialize with the shared Web-defaults options so the builder deserializes the
        // exact same shape (B1: a serializer mismatch silently nulls fields, not errors).
        return fresh.Select(q => JsonSerializer.Serialize(q, QuakeJson.Options)).ToArray();
    }
}
