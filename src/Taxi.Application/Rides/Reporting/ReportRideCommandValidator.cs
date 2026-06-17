using FluentValidation;

namespace Taxi.Application.Rides.Reporting;

/// <summary>
/// Règles de validation de <see cref="ReportRideCommand"/> : le motif du signalement ne peut pas être vide.
/// </summary>
internal sealed class ReportRideCommandValidator : AbstractValidator<ReportRideCommand>
{
    public ReportRideCommandValidator()
    {
        RuleFor(c => c.Reason).NotEmpty();
    }
}
