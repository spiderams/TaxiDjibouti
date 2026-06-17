using Ardalis.Specification;
using Taxi.Domain.Rides;

namespace Taxi.Application.Rides;

/// <summary>
/// Spécification : sélectionne une course par son identifiant unique.
/// </summary>
internal sealed class RideByIdSpec : Specification<Ride>
{
    public RideByIdSpec(int rideId) => Query.Where(r => r.Id == rideId);
}

/// <summary>
/// Spécification : sélectionne toutes les courses d'un client, triées de la plus récente à la plus ancienne.
/// </summary>
internal sealed class RidesByClientSpec : Specification<Ride>
{
    public RidesByClientSpec(string clientId)
        => Query.Where(r => r.ClientId == clientId).OrderByDescending(r => r.CreatedAt);
}

/// <summary>
/// Spécification : sélectionne toutes les courses assignées à un chauffeur, triées de la plus récente à la plus ancienne.
/// </summary>
internal sealed class RidesByDriverSpec : Specification<Ride>
{
    public RidesByDriverSpec(int driverId)
        => Query.Where(r => r.DriverId == driverId).OrderByDescending(r => r.CreatedAt);
}

/// <summary>
/// Spécification : sélectionne toutes les courses en attente de chauffeur (statut <c>Pending</c>), triées de la plus récente à la plus ancienne.
/// </summary>
internal sealed class PendingRidesSpec : Specification<Ride>
{
    public PendingRidesSpec()
        => Query.Where(r => r.Status == RideStatus.Pending).OrderByDescending(r => r.CreatedAt);
}

/// <summary>
/// Spécification : sélectionne les courses en offre dont le délai d'acceptation est expiré à l'instant <c>now</c>.
/// </summary>
public sealed class ExpiredOffersSpec : Specification<Ride>
{
    public ExpiredOffersSpec(DateTime now)
        => Query.Where(r => r.Status == RideStatus.Offered && r.OfferExpiresAt != null && r.OfferExpiresAt <= now);
}
