namespace Taxi.Application.Realtime.UpdateDriverLocation;

/// <summary>
/// Projection de la position du chauffeur diffusée en temps réel au client : coordonnées GPS, cap, vitesse
/// et horodatage d'émission.
/// </summary>
public sealed record DriverLocationBroadcast(
    int RideId,
    string ClientId,
    int DriverId,
    double Latitude,
    double Longitude,
    double? Heading,
    double? Speed,
    DateTime SentAt);
