using Microsoft.Extensions.Logging;
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Application.Realtime;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Accept;

/// <summary>
/// Gère <see cref="AcceptRideCommand"/> : vérifie la disponibilité du chauffeur, attribue la course
/// et notifie le client en temps réel.
/// </summary>
internal sealed partial class AcceptRideCommandHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers,
    IRealtimeNotifier notifier,
    ILogger<AcceptRideCommandHandler> logger)
    : ICommandHandler<AcceptRideCommand, RideDto>
{
    public async Task<Result<RideDto>> Handle(AcceptRideCommand command, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.DriverUserId), cancellationToken);
        if (driver is null)
            return Result.Failure<RideDto>(RideErrors.NoDriverProfile);
        if (!driver.IsAvailable)
            return Result.Failure<RideDto>(RideErrors.DriverNotAvailable);

        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<RideDto>(RideErrors.NotFound);

        var accepted = ride.Accept(driver.Id);
        if (accepted.IsFailure)
            return Result.Failure<RideDto>(accepted.Error);

        await rides.UpdateAsync(ride, cancellationToken);
        driver.SetAvailability(false);
        await drivers.UpdateAsync(driver, cancellationToken);
        LogRideAccepted(logger, ride.Id, driver.Id);
        await notifier.RideStatusChangedAsync(ride.Id, ride.ClientId, ride.DriverId, ride.Status.ToString(), cancellationToken);
        return RideDto.From(ride);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Course {RideId} acceptée par le chauffeur {DriverId} (rendu indisponible)")]
    private static partial void LogRideAccepted(ILogger logger, int rideId, int driverId);
}
