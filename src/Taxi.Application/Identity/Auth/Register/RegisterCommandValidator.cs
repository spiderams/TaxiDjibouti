using FluentValidation;
using Taxi.Domain.Identity;

namespace Taxi.Application.Identity.Auth.Register;

/// <summary>
/// Règles de validation de <see cref="RegisterCommand"/>.
/// </summary>
internal sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(c => c.FullName).NotEmpty();
        RuleFor(c => c.PhoneNumber).NotEmpty();
        RuleFor(c => c.Password).NotEmpty().MinimumLength(6);
        RuleFor(c => c.Role).Must(role => RoleNames.All.Contains(role))
            .WithMessage("Rôle invalide (attendu: Client, Driver ou Admin).");
    }
}
