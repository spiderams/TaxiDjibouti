using NetTopologySuite.Geometries;
using Taxi.SharedKernel;

namespace Taxi.Domain.Drivers;

/// <summary>
/// Agrégat représentant un chauffeur enregistré dans la plateforme : regroupe son profil (véhicule, numéro de licence),
/// sa disponibilité, sa position géographique en temps réel et sa note moyenne calculée à partir des évaluations clients.
/// </summary>
public sealed class Driver : Entity
{
    public string UserId { get; private set; } = string.Empty;
    public string LicenseNumber { get; private set; } = string.Empty;
    public string VehiclePlate { get; private set; } = string.Empty;
    public string VehicleType { get; private set; } = "Taxi";
    public bool IsAvailable { get; private set; }
    public double AverageRating { get; private set; }
    public Point? LastLocation { get; private set; }
    public DateTime? LastLocationAt { get; private set; }

    public double? LastLatitude => LastLocation?.Y;
    public double? LastLongitude => LastLocation?.X;

    private Driver() { } // EF

    /// <summary>
    /// Crée un nouveau profil chauffeur associé à un utilisateur identité existant.
    /// </summary>
    public static Driver Create(string userId, string licenseNumber, string vehiclePlate, string vehicleType)
        => new()
        {
            UserId = userId,
            LicenseNumber = licenseNumber,
            VehiclePlate = vehiclePlate,
            VehicleType = vehicleType
        };

    /// <summary>
    /// Met à jour les informations du véhicule et de la licence du chauffeur.
    /// </summary>
    public void UpdateProfile(string licenseNumber, string vehiclePlate, string vehicleType)
    {
        LicenseNumber = licenseNumber;
        VehiclePlate = vehiclePlate;
        VehicleType = vehicleType;
    }

    /// <summary>
    /// Bascule la disponibilité du chauffeur : seul un chauffeur disponible peut recevoir de nouvelles courses.
    /// </summary>
    public void SetAvailability(bool isAvailable) => IsAvailable = isAvailable;

    /// <summary>
    /// Recalcule et enregistre la note moyenne du chauffeur après chaque nouvelle évaluation client.
    /// </summary>
    public void UpdateAverageRating(double average) => AverageRating = average;

    /// <summary>
    /// Enregistre la position GPS courante du chauffeur en coordonnées WGS-84 (SRID 4326),
    /// utilisée pour le suivi temps réel et l'affectation des courses à proximité.
    /// </summary>
    public void UpdateLocation(double latitude, double longitude)
    {
        LastLocation = new Point(longitude, latitude) { SRID = 4326 };
        LastLocationAt = DateTime.UtcNow;
    }
}
