using Taxi.Application.Abstractions;
using Taxi.Domain.Drivers;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Drivers.UpsertProfile;

/// <summary>
/// Gère <see cref="UpsertDriverProfileCommand"/> : crée le profil s'il n'existe pas, le met à jour sinon.
/// </summary>
internal sealed class UpsertDriverProfileCommandHandler(IRepository<Driver> drivers)
    : ICommandHandler<UpsertDriverProfileCommand, DriverDto>
{
    public async Task<Result<DriverDto>> Handle(UpsertDriverProfileCommand command, CancellationToken cancellationToken)
    {
        var existing = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.UserId), cancellationToken);

        if (existing is null)
        {
            var created = Driver.Create(command.UserId, command.LicenseNumber, command.VehiclePlate, command.VehicleType);
            await drivers.AddAsync(created, cancellationToken);
            return DriverDto.From(created);
        }

        existing.UpdateProfile(command.LicenseNumber, command.VehiclePlate, command.VehicleType);
        await drivers.UpdateAsync(existing, cancellationToken);
        return DriverDto.From(existing);
    }
}
