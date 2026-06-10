using Quake.Core.Models;

namespace Quake.Core.Abstractions;

public interface IPhotoClient
{
    Task<IReadOnlyList<PhotoInfo>> SearchAsync(string query, int count = 3, CancellationToken ct = default);
}
