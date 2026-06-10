namespace Quake.Core.Models;

public sealed record HistoricalContext(int QuakesLast30DaysWithin300Km, double? MaxMagnitudeLastYear);
