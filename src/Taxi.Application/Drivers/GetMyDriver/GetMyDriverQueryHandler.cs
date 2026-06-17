using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Domain.Drivers;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Drivers.GetMyDriver;

/// <summary>
/// Gère <see cref="GetMyDriverQuery"/> : recherche le profil chauffeur par UserId et retourne son DTO.
/// </summary>
internal sealed class GetMyDriverQueryHandler(IRepository<Driver> drivers)
    : IQueryHandler<GetMyDriverQuery, DriverDto>
{
    public async Task<Result<DriverDto>> Handle(GetMyDriverQuery query, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(query.UserId), cancellationToken);
        return driver is null
            ? Result.Failure<DriverDto>(Error.NotFound("Driver.NotFound", "Profil chauffeur introuvable."))
            : DriverDto.From(driver);
    }
}
