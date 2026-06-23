using Microsoft.EntityFrameworkCore;
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

        // Capture des perdants AVANT mutation (la vague est vidée par AcceptOffer).
        var losers = ride.OfferedDriverIds.Where(id => id != driver.Id).ToList();

        var accepted = ride.AcceptOffer(driver.Id);
        if (accepted.IsFailure)
            return Result.Failure<RideDto>(accepted.Error);

        try
        {
            await rides.UpdateAsync(ride, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Un autre chauffeur de la vague a gagné la course entre la lecture et l'écriture.
            return Result.Failure<RideDto>(RideErrors.OfferTaken);
        }

        driver.SetAvailability(false);
        await drivers.UpdateAsync(driver, cancellationToken);
        LogOfferAccepted(logger, ride.Id, driver.Id);

        await RevokeLosersAsync(losers, ride.Id, "taken", cancellationToken);
        await notifier.RideStatusChangedAsync(ride.Id, ride.ClientId, ride.DriverId, ride.Status.ToString(), cancellationToken);
        return RideDto.From(ride);
    }

    /// <summary>
    /// Révoque l'offre auprès des chauffeurs perdants de la vague en résolvant leur identifiant utilisateur SignalR.
    /// </summary>
    private async Task RevokeLosersAsync(IReadOnlyCollection<int> loserDriverIds, int rideId, string reason, CancellationToken cancellationToken)
    {
        if (loserDriverIds.Count == 0)
            return;

        var losers = await drivers.ListAsync(new DriversByIdsSpec(loserDriverIds), cancellationToken);
        foreach (var loser in losers)
            await notifier.RideOfferRevokedAsync(loser.UserId, rideId, reason, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Offre de la course {RideId} acceptée par le chauffeur {DriverId}")]
    private static partial void LogOfferAccepted(ILogger logger, int rideId, int driverId);
}
