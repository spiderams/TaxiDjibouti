using Taxi.SharedKernel;

namespace Taxi.Domain.Rides;

/// <summary>
/// Évaluation laissée par un client à l'issue d'une course terminée : contient la note (1–5)
/// et un commentaire optionnel. Elle alimente le calcul de la note moyenne du chauffeur.
/// </summary>
public sealed class Rating : Entity
{
    public int RideId { get; private set; }
    public string ClientId { get; private set; } = string.Empty;
    public int DriverId { get; private set; }
    public int Score { get; private set; }
    public string? Comment { get; private set; }

    private Rating() { } // EF

    /// <summary>
    /// Crée une nouvelle évaluation pour une course donnée, en liant client, chauffeur, note et commentaire.
    /// </summary>
    public static Rating Create(int rideId, string clientId, int driverId, int score, string? comment)
        => new() { RideId = rideId, ClientId = clientId, DriverId = driverId, Score = score, Comment = comment };

    /// <summary>
    /// Modifie la note et le commentaire d'une évaluation existante, permettant au client de corriger son avis.
    /// </summary>
    public void UpdateScore(int score, string? comment)
    {
        Score = score;
        Comment = comment;
    }
}
