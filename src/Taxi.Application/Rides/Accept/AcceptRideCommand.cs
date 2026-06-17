using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Accept;

/// <summary>
/// Commande permettant à un chauffeur d'accepter une course en attente et de se l'attribuer.
/// </summary>
public sealed record AcceptRideCommand(int RideId, string DriverUserId) : ICommand<RideDto>;
