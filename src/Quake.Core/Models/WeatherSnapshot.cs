namespace Quake.Core.Models;

public sealed record WeatherSnapshot(double TemperatureC, double WindSpeedKmh, int WeatherCode, string Description);
