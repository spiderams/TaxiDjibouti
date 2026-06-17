using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Realtime.UpdateDriverLocation;

/// <summary>
/// Commande envoyée par le chauffeur pour mettre à jour sa position GPS en cours de course.
/// </summary>
public sealed record UpdateDriverLocationCommand(
    string DriverUserId,
    int RideId,
    double Latitude,
    double Longitude,
    double? Heading,
    double? Speed) : ICommand<DriverLocationBroadcast>;
