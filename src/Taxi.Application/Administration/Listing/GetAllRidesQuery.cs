using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Administration.Listing;

/// <summary>
/// Requête administrative retournant l'intégralité des courses enregistrées dans le système.
/// </summary>
public sealed record GetAllRidesQuery : IQuery<IReadOnlyList<RideDto>>;
