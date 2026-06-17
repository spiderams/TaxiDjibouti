using FluentValidation;

namespace Taxi.Application.Identity.Auth.Login;

/// <summary>
/// Règles de validation de <see cref="LoginCommand"/>.
/// </summary>
internal sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(c => c.PhoneNumber).NotEmpty();
        RuleFor(c => c.Password).NotEmpty();
    }
}
