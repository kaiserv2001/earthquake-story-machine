using Quake.Core.Models;

namespace Quake.Core.Abstractions;

public interface IGeocodingClient
{
    Task<LocationInfo?> ReverseGeocodeAsync(double lat, double lon, CancellationToken ct = default);
}
