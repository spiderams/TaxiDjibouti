using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Application.Rides;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Realtime.UpdateDriverLocation;

/// <summary>
/// Gère <see cref="UpdateDriverLocationCommand"/> : vérifie que le chauffeur est bien assigné à la course active
/// puis persiste la nouvelle position et retourne les données à diffuser au client.
/// </summary>
internal sealed class UpdateDriverLocationCommandHandler(
    IRepository<Driver> drivers,
    IRepository<Ride> rides)
    : ICommandHandler<UpdateDriverLocationCommand, DriverLocationBroadcast>
{
    public async Task<Result<DriverLocationBroadcast>> Handle(
        UpdateDriverLocationCommand command, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.DriverUserId), cancellationToken);
        if (driver is null)
            return Result.Failure<DriverLocationBroadcast>(RealtimeErrors.DriverNotFound);

        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<DriverLocationBroadcast>(RideErrors.NotFound);

        if (ride.DriverId != driver.Id)
            return Result.Failure<DriverLocationBroadcast>(RealtimeErrors.RideNotAssigned);

        if (ride.Status is RideStatus.Completed or RideStatus.Cancelled)
            return Result.Failure<DriverLocationBroadcast>(RealtimeErrors.RideNotActive);

        driver.UpdateLocation(command.Latitude, command.Longitude);
        await drivers.UpdateAsync(driver, cancellationToken);

        return new DriverLocationBroadcast(
            ride.Id, ride.ClientId, driver.Id,
            command.Latitude, command.Longitude, command.Heading, command.Speed,
            DateTime.UtcNow);
    }
}
