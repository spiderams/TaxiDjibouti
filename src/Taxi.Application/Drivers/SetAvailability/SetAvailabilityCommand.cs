using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Drivers.SetAvailability;

/// <summary>
/// Commande permettant à un chauffeur de basculer son statut de disponibilité (disponible / hors-ligne).
/// </summary>
public sealed record SetAvailabilityCommand(string UserId, bool IsAvailable) : ICommand<DriverDto>;
