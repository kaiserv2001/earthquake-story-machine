using Quake.Core.Models;

namespace Quake.Core.Abstractions;

public interface IStoryCardStore
{
    /// <returns>Blob path of the stored card.</returns>
    Task<string> SaveAsync(StoryCard card, CancellationToken ct = default);
    Task<StoryCard?> GetAsync(string quakeId, CancellationToken ct = default);
}
