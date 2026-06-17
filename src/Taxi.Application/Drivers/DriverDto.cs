using Taxi.Domain.Drivers;

namespace Taxi.Application.Drivers;

/// <summary>
/// Projection publique d'un profil chauffeur : identifiants, véhicule, disponibilité et note moyenne.
/// </summary>
public sealed record DriverDto(
    int Id,
    string UserId,
    string LicenseNumber,
    string VehiclePlate,
    string VehicleType,
    bool IsAvailable,
    double AverageRating)
{
    public static DriverDto From(Driver driver) => new(
        driver.Id, driver.UserId, driver.LicenseNumber, driver.VehiclePlate,
        driver.VehicleType, driver.IsAvailable, driver.AverageRating);
}
