using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Transitions;

/// <summary>
/// Commande signalant le début effectif du trajet : le client est monté à bord et la course démarre.
/// </summary>
public sealed record StartRideCommand(int RideId, string DriverUserId) : ICommand<RideDto>;
