using FluentValidation;

namespace Taxi.Application.Rides.Rate;

/// <summary>
/// Règles de validation de <see cref="RateRideCommand"/> : le score doit être compris entre 1 et 5 inclus.
/// </summary>
internal sealed class RateRideCommandValidator : AbstractValidator<RateRideCommand>
{
    public RateRideCommandValidator()
    {
        RuleFor(c => c.Score).InclusiveBetween(1, 5);
    }
}
