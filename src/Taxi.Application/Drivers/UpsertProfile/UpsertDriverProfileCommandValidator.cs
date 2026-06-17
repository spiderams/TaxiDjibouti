using FluentValidation;

namespace Taxi.Application.Drivers.UpsertProfile;

/// <summary>
/// Règles de validation de <see cref="UpsertDriverProfileCommand"/>.
/// </summary>
internal sealed class UpsertDriverProfileCommandValidator : AbstractValidator<UpsertDriverProfileCommand>
{
    public UpsertDriverProfileCommandValidator()
    {
        RuleFor(c => c.LicenseNumber).NotEmpty();
        RuleFor(c => c.VehiclePlate).NotEmpty();
        RuleFor(c => c.VehicleType).NotEmpty();
    }
}
