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
