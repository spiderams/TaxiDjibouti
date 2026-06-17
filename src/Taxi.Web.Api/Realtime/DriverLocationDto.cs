namespace Taxi.Web.Api.Realtime;

/// <summary>
/// Position transmise par le chauffeur via le hub (course, lat/lon, cap, vitesse).
/// </summary>
public sealed record DriverLocationDto(int RideId, double Latitude, double Longitude, double? Heading, double? Speed);
