using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Transitions;

/// <summary>
/// Commande permettant au chauffeur de marquer une course comme terminée et de se remettre disponible.
/// </summary>
public sealed record CompleteRideCommand(int RideId, string DriverUserId) : ICommand<RideDto>;
