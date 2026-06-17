using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Cancel;

/// <summary>
/// Commande d'annulation d'une course, utilisable aussi bien par le client que par le chauffeur assigné.
/// </summary>
public sealed record CancelRideCommand(int RideId, string UserId, bool IsDriver) : ICommand<RideDto>;
