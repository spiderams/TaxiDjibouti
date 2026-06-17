using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Application.Realtime;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Cancel;

/// <summary>
/// Gère <see cref="CancelRideCommand"/> : annule la course selon l'initiateur (client ou chauffeur),
/// remet le chauffeur disponible si nécessaire et notifie en temps réel.
/// </summary>
internal sealed class CancelRideCommandHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers,
    IRealtimeNotifier notifier)
    : ICommandHandler<CancelRideCommand, RideDto>
{
    public async Task<Result<RideDto>> Handle(CancelRideCommand command, CancellationToken cancellationToken)
    {
        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<RideDto>(RideErrors.NotFound);

        Result outcome;
        if (command.IsDriver)
        {
            var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.UserId), cancellationToken);
            if (driver is null)
                return Result.Failure<RideDto>(RideErrors.NoDriverProfile);
            if (ride.DriverId != driver.Id)
                return Result.Failure<RideDto>(RideErrors.NotAssignedDriver);
            outcome = ride.CancelByDriver();
        }
        else
        {
            if (ride.ClientId != command.UserId)
                return Result.Failure<RideDto>(RideErrors.NotAssignedDriver);
            outcome = ride.CancelByClient();
        }

        if (outcome.IsFailure)
            return Result.Failure<RideDto>(outcome.Error);

        await rides.UpdateAsync(ride, cancellationToken);
        if (ride.DriverId is not null)
        {
            var assigned = await drivers.FirstOrDefaultAsync(new DriverByIdSpec(ride.DriverId.Value), cancellationToken);
            if (assigned is not null)
            {
                assigned.SetAvailability(true);
                await drivers.UpdateAsync(assigned, cancellationToken);
            }
        }
        await notifier.RideStatusChangedAsync(ride.Id, ride.ClientId, ride.DriverId, ride.Status.ToString(), cancellationToken);
        return RideDto.From(ride);
    }
}
