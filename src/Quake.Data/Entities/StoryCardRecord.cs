namespace Quake.Data.Entities;

public class StoryCardRecord
{
    public int Id { get; set; }
    public required string QuakeId { get; set; }     // unique index — dedup key
    public double Magnitude { get; set; }
    public required string Place { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTimeOffset OccurredUtc { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public required string BlobPath { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}
