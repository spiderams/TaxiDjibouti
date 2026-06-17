using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Domain.Drivers;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Drivers.SetAvailability;

/// <summary>
/// Gère <see cref="SetAvailabilityCommand"/> : met à jour la disponibilité du chauffeur et retourne son profil actualisé.
/// </summary>
internal sealed class SetAvailabilityCommandHandler(IRepository<Driver> drivers)
    : ICommandHandler<SetAvailabilityCommand, DriverDto>
{
    public async Task<Result<DriverDto>> Handle(SetAvailabilityCommand command, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.UserId), cancellationToken);
        if (driver is null)
            return Result.Failure<DriverDto>(Error.NotFound("Driver.NotFound", "Profil chauffeur introuvable."));

        driver.SetAvailability(command.IsAvailable);
        await drivers.UpdateAsync(driver, cancellationToken);
        return DriverDto.From(driver);
    }
}
