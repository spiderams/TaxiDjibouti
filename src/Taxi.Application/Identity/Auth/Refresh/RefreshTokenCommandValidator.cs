using FluentValidation;

namespace Taxi.Application.Identity.Auth.Refresh;

/// <summary>
/// Règles de validation de <see cref="RefreshTokenCommand"/>.
/// </summary>
internal sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(c => c.RefreshToken).NotEmpty();
    }
}
