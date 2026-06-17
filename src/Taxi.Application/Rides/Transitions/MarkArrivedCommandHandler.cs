using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Application.Realtime;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Transitions;

/// <summary>
/// Gère <see cref="MarkArrivedCommand"/> : applique la transition d'état et notifie le client
/// que son chauffeur est sur place.
/// </summary>
internal sealed class MarkArrivedCommandHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers,
    IRealtimeNotifier notifier)
    : ICommandHandler<MarkArrivedCommand, RideDto>
{
    public async Task<Result<RideDto>> Handle(MarkArrivedCommand command, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.DriverUserId), cancellationToken);
        if (driver is null)
            return Result.Failure<RideDto>(RideErrors.NoDriverProfile);

        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<RideDto>(RideErrors.NotFound);
        if (ride.DriverId != driver.Id)
            return Result.Failure<RideDto>(RideErrors.NotAssignedDriver);

        var transition = ride.MarkArrived();
        if (transition.IsFailure)
            return Result.Failure<RideDto>(transition.Error);

        await rides.UpdateAsync(ride, cancellationToken);
        await notifier.RideStatusChangedAsync(ride.Id, ride.ClientId, ride.DriverId, ride.Status.ToString(), cancellationToken);
        return RideDto.From(ride);
    }
}
