using Taxi.Application.Abstractions;
using Taxi.Application.Dispatch;
using Taxi.Application.Pricing.EstimatePrice;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Request;

/// <summary>
/// Crée une course (prix estimé via la tarification) puis déclenche l'auto-dispatch vers le chauffeur disponible le plus proche.
/// </summary>
internal sealed class RequestRideCommandHandler(
    IRepository<Ride> rides,
    IQueryHandler<EstimatePriceQuery, EstimatePriceResponse> priceEstimator,
    IRideDispatcher dispatcher)
    : ICommandHandler<RequestRideCommand, RideDto>
{
    public async Task<Result<RideDto>> Handle(RequestRideCommand command, CancellationToken cancellationToken)
    {
        var price = await priceEstimator.Handle(
            new EstimatePriceQuery(command.PickupZone, command.DestinationZone), cancellationToken);
        if (price.IsFailure)
            return Result.Failure<RideDto>(price.Error);

        var ride = Ride.Request(
            command.ClientId, command.PickupAddress, command.DestinationAddress,
            command.PickupZone, command.DestinationZone,
            command.PickupLatitude, command.PickupLongitude,
            command.DestinationLatitude, command.DestinationLongitude,
            price.Value.Price);

        await rides.AddAsync(ride, cancellationToken);
        await dispatcher.DispatchAsync(ride.Id, cancellationToken);
        return RideDto.From(ride);
    }
}
