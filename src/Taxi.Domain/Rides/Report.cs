using Taxi.SharedKernel;

namespace Taxi.Domain.Rides;

/// <summary>
/// Signalement soumis par un client à propos d'une course : capture la raison du litige
/// et une description libre, afin que l'équipe d'administration puisse traiter les incidents
/// (comportement inappropriate, trajet anormal, etc.).
/// </summary>
public sealed class Report : Entity
{
    public int RideId { get; private set; }
    public string ClientId { get; private set; } = string.Empty;
    public int? DriverId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    private Report() { } // EF

    /// <summary>
    /// Crée un nouveau signalement associant une course, le client déclarant, le chauffeur concerné
    /// (si connu), la raison principale et une description détaillée optionnelle.
    /// </summary>
    public static Report Create(int rideId, string clientId, int? driverId, string reason, string? description)
        => new() { RideId = rideId, ClientId = clientId, DriverId = driverId, Reason = reason, Description = description };
}
