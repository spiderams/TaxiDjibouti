using Microsoft.Extensions.Logging;
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Application.Realtime;
using Taxi.Application.Rides;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Dispatch.AcceptOffer;

/// <summary>
/// Gère <see cref="AcceptOfferCommand"/> : confirme l'acceptation de l'offre, rend le chauffeur indisponible
/// et notifie le client en temps réel.
/// </summary>
internal sealed partial class AcceptOfferCommandHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers,
    IRealtimeNotifier notifier,
    ILogger<AcceptOfferCommandHandler> logger)
    : ICommandHandler<AcceptOfferCommand, RideDto>
{
    public async Task<Result<RideDto>> Handle(AcceptOfferCommand command, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.DriverUserId), cancellationToken);
        if (driver is null)
            return Result.Failure<RideDto>(RideErrors.NoDriverProfile);

        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<RideDto>(RideErrors.NotFound);

        var accepted = ride.AcceptOffer(driver.Id);
        if (accepted.IsFailure)
            return Result.Failure<RideDto>(accepted.Error);

        await rides.UpdateAsync(ride, cancellationToken);

        driver.SetAvailability(false);
        await drivers.UpdateAsync(driver, cancellationToken);
        LogOfferAccepted(logger, ride.Id, driver.Id);

        await notifier.RideStatusChangedAsync(ride.Id, ride.ClientId, ride.DriverId, ride.Status.ToString(), cancellationToken);
        return RideDto.From(ride);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Offre de la course {RideId} acceptée par le chauffeur {DriverId}")]
    private static partial void LogOfferAccepted(ILogger logger, int rideId, int driverId);
}
