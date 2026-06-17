using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Request;

/// <summary>
/// Commande de création d'une course : porte les adresses, les zones tarifaires et les coordonnées GPS
/// optionnelles de prise en charge et de destination.
/// </summary>
public sealed record RequestRideCommand(
    string ClientId,
    string PickupAddress, string DestinationAddress,
    string PickupZone, string DestinationZone,
    double? PickupLatitude, double? PickupLongitude,
    double? DestinationLatitude, double? DestinationLongitude)
    : ICommand<RideDto>;
