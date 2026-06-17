using Taxi.Application.Abstractions;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Pending;

/// <summary>
/// Gère <see cref="GetPendingRidesQuery"/> : retourne la liste des courses dont le statut est <c>Pending</c>.
/// </summary>
internal sealed class GetPendingRidesQueryHandler(IRepository<Ride> rides)
    : IQueryHandler<GetPendingRidesQuery, IReadOnlyList<RideDto>>
{
    public async Task<Result<IReadOnlyList<RideDto>>> Handle(GetPendingRidesQuery query, CancellationToken cancellationToken)
    {
        var list = await rides.ListAsync(new PendingRidesSpec(), cancellationToken);
        return list.Select(RideDto.From).ToList();
    }
}
