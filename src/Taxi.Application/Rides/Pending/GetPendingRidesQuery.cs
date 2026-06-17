using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Pending;

/// <summary>
/// Requête qui retourne toutes les courses en attente de chauffeur, destinée au tableau de bord des chauffeurs.
/// </summary>
public sealed record GetPendingRidesQuery : IQuery<IReadOnlyList<RideDto>>;
