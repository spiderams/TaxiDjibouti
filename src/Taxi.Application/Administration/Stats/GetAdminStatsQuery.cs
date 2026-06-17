using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Administration.Stats;

/// <summary>
/// Requête retournant les statistiques globales de la plateforme à destination du tableau de bord administrateur.
/// </summary>
public sealed record GetAdminStatsQuery : IQuery<AdminStatsDto>;
