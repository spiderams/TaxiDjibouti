using Taxi.Domain.Rides;

namespace Taxi.Application.Rides;

/// <summary>
/// Projection d'une évaluation de course : porte le score (1-5), le commentaire optionnel
/// et les identifiants du client et du chauffeur concernés.
/// </summary>
public sealed record RatingDto(int Id, int RideId, string ClientId, int DriverId, int Score, string? Comment, DateTime CreatedAt)
{
    public static RatingDto From(Rating r) => new(r.Id, r.RideId, r.ClientId, r.DriverId, r.Score, r.Comment, r.CreatedAt);
}
