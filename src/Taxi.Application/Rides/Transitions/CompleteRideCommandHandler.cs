using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Application.Realtime;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Transitions;

/// <summary>
/// Gère <see cref="CompleteRideCommand"/> : valide l'assignation du chauffeur, clôture la course
/// et remet le chauffeur disponible avant notification temps réel.
/// </summary>
internal sealed class CompleteRideCommandHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers,
    IRealtimeNotifier notifier)
    : ICommandHandler<CompleteRideCommand, RideDto>
{
    public async Task<Result<RideDto>> Handle(CompleteRideCommand command, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.DriverUserId), cancellationToken);
        if (driver is null)
            return Result.Failure<RideDto>(RideErrors.NoDriverProfile);

        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<RideDto>(RideErrors.NotFound);
        if (ride.DriverId != driver.Id)
            return Result.Failure<RideDto>(RideErrors.NotAssignedDriver);

        var transition = ride.Complete();
        if (transition.IsFailure)
            return Result.Failure<RideDto>(transition.Error);

        await rides.UpdateAsync(ride, cancellationToken);
        driver.SetAvailability(true);
        await drivers.UpdateAsync(driver, cancellationToken);
        await notifier.RideStatusChangedAsync(ride.Id, ride.ClientId, ride.DriverId, ride.Status.ToString(), cancellationToken);
        return RideDto.From(ride);
    }
}
