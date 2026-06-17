using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Domain.Drivers;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Administration.Listing;

/// <summary>
/// Gère <see cref="GetDriversQuery"/> : charge tous les profils chauffeurs et les projette en <see cref="DriverDto"/>.
/// </summary>
internal sealed class GetDriversQueryHandler(IRepository<Driver> drivers)
    : IQueryHandler<GetDriversQuery, IReadOnlyList<DriverDto>>
{
    public async Task<Result<IReadOnlyList<DriverDto>>> Handle(GetDriversQuery query, CancellationToken cancellationToken)
    {
        var list = await drivers.ListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<DriverDto>>(list.Select(DriverDto.From).ToList());
    }
}
