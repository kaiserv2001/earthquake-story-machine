namespace Quake.Core.Models;

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
