using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Application.Rides;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Realtime.RideAccess;

/// <summary>
/// Gère <see cref="RideAccessQuery"/> : autorise les admins systématiquement, puis vérifie que l'utilisateur
/// est client ou chauffeur de la course demandée.
/// </summary>
internal sealed class RideAccessQueryHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers)
    : IQueryHandler<RideAccessQuery, bool>
{
    public async Task<Result<bool>> Handle(RideAccessQuery query, CancellationToken cancellationToken)
    {
        if (query.IsAdmin)
            return true;

        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(query.RideId), cancellationToken);
        if (ride is null)
            return false;

        if (ride.ClientId == query.UserId)
            return true;

        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(query.UserId), cancellationToken);
        return driver is not null && ride.DriverId == driver.Id;
    }
}
