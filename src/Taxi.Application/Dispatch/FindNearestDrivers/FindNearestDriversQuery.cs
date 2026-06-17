using Taxi.Application.Dispatch;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Dispatch.FindNearestDrivers;

/// <summary>
/// Requête géospatiale retournant les chauffeurs disponibles dans un rayon donné autour d'un point GPS,
/// triés par distance croissante.
/// </summary>
public sealed record FindNearestDriversQuery(double Lat, double Lon, double RadiusMeters, int Max)
    : IQuery<IReadOnlyList<NearbyDriver>>;
