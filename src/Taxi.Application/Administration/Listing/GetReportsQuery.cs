using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Administration.Listing;

/// <summary>
/// Requête administrative retournant tous les signalements déposés par les clients.
/// </summary>
public sealed record GetReportsQuery : IQuery<IReadOnlyList<ReportDto>>;
