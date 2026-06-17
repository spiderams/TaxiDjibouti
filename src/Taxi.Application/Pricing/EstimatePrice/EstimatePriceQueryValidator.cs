using FluentValidation;

namespace Taxi.Application.Pricing.EstimatePrice;

/// <summary>
/// Règles de validation de <see cref="EstimatePriceQuery"/>.
/// </summary>
internal sealed class EstimatePriceQueryValidator : AbstractValidator<EstimatePriceQuery>
{
    public EstimatePriceQueryValidator()
    {
        RuleFor(q => q.FromZone).NotEmpty();
        RuleFor(q => q.ToZone).NotEmpty();
    }
}
