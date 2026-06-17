namespace Taxi.Application.Dispatch;

/// <summary>
/// Recherche géospatiale (PostGIS) des chauffeurs disponibles les plus proches d'un point. Implémentée en Infrastructure (accès EF/NetTopologySuite).
/// </summary>
public interface IDriverLocator
{
    Task<IReadOnlyList<NearbyDriver>> FindNearestAsync(
        double latitude, double longitude, double radiusMeters, int max, CancellationToken cancellationToken);
}

/// <summary>
/// Chauffeur proche + distance en mètres, renvoyé par la recherche de proximité.
/// </summary>
public sealed record NearbyDriver(
    int DriverId, string UserId, double DistanceMeters,
    double Latitude, double Longitude, string VehicleType);
