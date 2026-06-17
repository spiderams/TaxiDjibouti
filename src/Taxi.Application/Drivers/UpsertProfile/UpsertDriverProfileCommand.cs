using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Drivers.UpsertProfile;

/// <summary>
/// Commande de création ou de mise à jour du profil chauffeur (permis, plaque d'immatriculation, type de véhicule).
/// </summary>
public sealed record UpsertDriverProfileCommand(
    string UserId, string LicenseNumber, string VehiclePlate, string VehicleType)
    : ICommand<DriverDto>;
