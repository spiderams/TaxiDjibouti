using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Application.Rides;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Dispatch.DeclineOffer;

/// <summary>
/// Gère <see cref="DeclineOfferCommand"/> : enregistre le chauffeur comme déjà sollicité, remet la course
/// en attente et relance le dispatch vers le prochain candidat disponible.
/// </summary>
internal sealed class DeclineOfferCommandHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers,
    IRideDispatcher dispatcher)
    : ICommandHandler<DeclineOfferCommand, RideDto>
{
    public async Task<Result<RideDto>> Handle(DeclineOfferCommand command, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.DriverUserId), cancellationToken);
        if (driver is null)
            return Result.Failure<RideDto>(RideErrors.NoDriverProfile);

        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<RideDto>(RideErrors.NotFound);

        ride.MarkDriverTried(driver.Id);
        var declined = ride.DeclineOffer(driver.Id);
        if (declined.IsFailure)
            return Result.Failure<RideDto>(declined.Error);

        await rides.UpdateAsync(ride, cancellationToken);

        // Si la vague est vidée, la course est repassée en Pending → on relance vers la vague suivante.
        if (ride.Status == RideStatus.Pending)
            await dispatcher.DispatchAsync(ride.Id, cancellationToken);

        return RideDto.From(ride);
    }
}
