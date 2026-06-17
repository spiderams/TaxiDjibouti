namespace Taxi.Application.Administration;

/// <summary>
/// Projection des indicateurs clés de l'administration : nombre total d'utilisateurs, de chauffeurs,
/// de courses et de signalements.
/// </summary>
public sealed record AdminStatsDto(int Users, int Drivers, int Rides, int Reports);
