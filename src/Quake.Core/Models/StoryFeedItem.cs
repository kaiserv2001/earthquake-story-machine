namespace Quake.Core.Models;

/// <summary>
/// A lightweight projection of a story card for the Atom feed. Built from the SQL
/// index (same source as <c>/api/cards</c>) so the feed never needs a blob fetch
/// per entry. Nullable fields mirror enrichment that may be absent.
/// </summary>
public sealed record StoryFeedItem
{
    public required string QuakeId { get; init; }
    public double Magnitude { get; init; }
    public required string Place { get; init; }
    public string? City { get; init; }
    public string? Country { get; init; }
    public DateTimeOffset OccurredUtc { get; init; }
}
