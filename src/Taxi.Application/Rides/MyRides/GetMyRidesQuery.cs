using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.MyRides;

/// <summary>
/// Requête qui retourne l'historique des courses de l'utilisateur courant, en tant que client ou chauffeur.
/// </summary>
public sealed record GetMyRidesQuery(string UserId, bool AsDriver) : IQuery<IReadOnlyList<RideDto>>;
