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
