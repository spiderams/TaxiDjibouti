using Taxi.Application.Dispatch;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Dispatch.FindNearestDrivers;

/// <summary>
/// Gère <see cref="FindNearestDriversQuery"/> : délègue la recherche géospatiale à <see cref="IDriverLocator"/>
/// et retourne la liste des chauffeurs proches.
/// </summary>
internal sealed class FindNearestDriversQueryHandler(IDriverLocator locator)
    : IQueryHandler<FindNearestDriversQuery, IReadOnlyList<NearbyDriver>>
{
    public async Task<Result<IReadOnlyList<NearbyDriver>>> Handle(
        FindNearestDriversQuery query, CancellationToken cancellationToken)
    {
        var drivers = await locator.FindNearestAsync(
            query.Lat, query.Lon, query.RadiusMeters, query.Max, cancellationToken);
        return Result.Success(drivers);
    }
}
