using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Transitions;

/// <summary>
/// Commande signalant que le chauffeur est arrivé au point de prise en charge du client.
/// </summary>
public sealed record MarkArrivedCommand(int RideId, string DriverUserId) : ICommand<RideDto>;
