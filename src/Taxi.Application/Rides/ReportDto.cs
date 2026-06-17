using Taxi.Domain.Rides;

namespace Taxi.Application.Rides;

/// <summary>
/// Projection d'un signalement déposé par un client : porte le motif, la description optionnelle
/// et les identifiants de la course et des parties impliquées.
/// </summary>
public sealed record ReportDto(int Id, int RideId, string ClientId, int? DriverId, string Reason, string? Description, DateTime CreatedAt)
{
    public static ReportDto From(Report r) => new(r.Id, r.RideId, r.ClientId, r.DriverId, r.Reason, r.Description, r.CreatedAt);
}
