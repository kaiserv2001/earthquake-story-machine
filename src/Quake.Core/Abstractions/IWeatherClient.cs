using Quake.Core.Models;

namespace Quake.Core.Abstractions;

public interface IWeatherClient
{
    Task<WeatherSnapshot?> GetCurrentAsync(double lat, double lon, CancellationToken ct = default);
}
