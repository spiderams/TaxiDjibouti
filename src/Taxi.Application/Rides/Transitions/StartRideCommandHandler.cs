using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Application.Realtime;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Transitions;

/// <summary>
/// Gère <see cref="StartRideCommand"/> : applique la transition vers l'état <c>InProgress</c>
/// et notifie les parties en temps réel.
/// </summary>
internal sealed class StartRideCommandHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers,
    IRealtimeNotifier notifier)
    : ICommandHandler<StartRideCommand, RideDto>
{
    public async Task<Result<RideDto>> Handle(StartRideCommand command, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.DriverUserId), cancellationToken);
        if (driver is null)
            return Result.Failure<RideDto>(RideErrors.NoDriverProfile);

        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<RideDto>(RideErrors.NotFound);
        if (ride.DriverId != driver.Id)
            return Result.Failure<RideDto>(RideErrors.NotAssignedDriver);

        var transition = ride.Start();
        if (transition.IsFailure)
            return Result.Failure<RideDto>(transition.Error);

        await rides.UpdateAsync(ride, cancellationToken);
        await notifier.RideStatusChangedAsync(ride.Id, ride.ClientId, ride.DriverId, ride.Status.ToString(), cancellationToken);
        return RideDto.From(ride);
    }
}
