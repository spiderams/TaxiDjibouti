using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.MyRides;

/// <summary>
/// Gère <see cref="GetMyRidesQuery"/> : charge les courses du client ou résout d'abord le profil chauffeur
/// avant de retourner ses courses assignées.
/// </summary>
internal sealed class GetMyRidesQueryHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers)
    : IQueryHandler<GetMyRidesQuery, IReadOnlyList<RideDto>>
{
    public async Task<Result<IReadOnlyList<RideDto>>> Handle(GetMyRidesQuery query, CancellationToken cancellationToken)
    {
        List<Ride> list;
        if (query.AsDriver)
        {
            var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(query.UserId), cancellationToken);
            list = driver is null
                ? new List<Ride>()
                : await rides.ListAsync(new RidesByDriverSpec(driver.Id), cancellationToken);
        }
        else
        {
            list = await rides.ListAsync(new RidesByClientSpec(query.UserId), cancellationToken);
        }

        return list.Select(RideDto.From).ToList();
    }
}
