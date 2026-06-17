using Ardalis.Specification;
using Taxi.Domain.Rides;

namespace Taxi.Application.Rides;

/// <summary>
/// Spécification : sélectionne la note associée à une course donnée.
/// </summary>
internal sealed class RatingByRideSpec : Specification<Rating>
{
    public RatingByRideSpec(int rideId) => Query.Where(r => r.RideId == rideId);
}

/// <summary>
/// Spécification : sélectionne toutes les notes reçues par un chauffeur, pour le calcul de sa moyenne.
/// </summary>
internal sealed class RatingsByDriverSpec : Specification<Rating>
{
    public RatingsByDriverSpec(int driverId) => Query.Where(r => r.DriverId == driverId);
}
