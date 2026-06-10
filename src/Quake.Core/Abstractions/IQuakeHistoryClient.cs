using Quake.Core.Models;

namespace Quake.Core.Abstractions;

public interface IQuakeHistoryClient
{
    Task<HistoricalContext?> GetHistoryAsync(double lat, double lon, DateTimeOffset before, CancellationToken ct = default);
}
