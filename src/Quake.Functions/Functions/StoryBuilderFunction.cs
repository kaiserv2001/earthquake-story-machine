using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quake.Core;
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
        // Deserialize with the same Web-defaults options the poller serialized with (B1).
        var quake = JsonSerializer.Deserialize<QuakeEvent>(message, QuakeJson.Options)
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
        logger.LogInformation("Story card created for M{Mag} {Place} -> {Blob}",
            quake.Magnitude, quake.Place, blobPath);
    }
}
