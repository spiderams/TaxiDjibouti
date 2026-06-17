using Taxi.Domain.Rides;

namespace Taxi.Application.Rides;

/// <summary>
/// Projection d'une course exposée aux couches supérieures : agrège les données d'identité, de localisation,
/// de tarification et de statut d'un trajet.
/// </summary>
public sealed record RideDto(
    int Id, string ClientId, int? DriverId,
    string PickupAddress, string DestinationAddress,
    string PickupZone, string DestinationZone,
    double? PickupLatitude, double? PickupLongitude,
    double? DestinationLatitude, double? DestinationLongitude,
    decimal EstimatedPrice, string Status,
    DateTime? AcceptedAt, DateTime? CompletedAt, DateTime CreatedAt)
{
    public static RideDto From(Ride r) => new(
        r.Id, r.ClientId, r.DriverId,
        r.PickupAddress, r.DestinationAddress,
        r.PickupZone, r.DestinationZone,
        r.PickupLatitude, r.PickupLongitude,
        r.DestinationLatitude, r.DestinationLongitude,
        r.EstimatedPrice, r.Status.ToString(),
        r.AcceptedAt, r.CompletedAt, r.CreatedAt);
}
