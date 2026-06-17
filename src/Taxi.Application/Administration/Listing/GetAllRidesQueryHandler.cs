using Taxi.Application.Abstractions;
using Taxi.Application.Rides;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Administration.Listing;

/// <summary>
/// Gère <see cref="GetAllRidesQuery"/> : charge toutes les courses et les projette en <see cref="RideDto"/>.
/// </summary>
internal sealed class GetAllRidesQueryHandler(IRepository<Ride> rides)
    : IQueryHandler<GetAllRidesQuery, IReadOnlyList<RideDto>>
{
    public async Task<Result<IReadOnlyList<RideDto>>> Handle(GetAllRidesQuery query, CancellationToken cancellationToken)
    {
        var list = await rides.ListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<RideDto>>(list.Select(RideDto.From).ToList());
    }
}
