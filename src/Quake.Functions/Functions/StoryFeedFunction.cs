using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Quake.Core.Models;
using Quake.Core.Services;
using Quake.Data;

namespace Quake.Functions.Functions;

public class StoryFeedFunction(QuakeDbContext db)
{
    private const string AtomContentType = "application/atom+xml; charset=utf-8";

    [Function("GetStoryFeed")]
    public async Task<IActionResult> Feed(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "feed")] HttpRequest req,
        CancellationToken ct)
    {
        // Same source, ordering, and cap as GET /api/cards so the feed and the list agree.
        var items = await db.StoryCards
            .OrderByDescending(s => s.OccurredUtc)
            .Take(50)
            .Select(s => new StoryFeedItem
            {
                QuakeId = s.QuakeId,
                Magnitude = s.Magnitude,
                Place = s.Place,
                City = s.City,
                Country = s.Country,
                OccurredUtc = s.OccurredUtc,
            })
            .ToListAsync(ct);

        var xml = AtomFeedBuilder.Build(items, req.GetEncodedUrl());

        return new ContentResult
        {
            Content = xml,
            ContentType = AtomContentType,
            StatusCode = StatusCodes.Status200OK,
        };
    }
}
