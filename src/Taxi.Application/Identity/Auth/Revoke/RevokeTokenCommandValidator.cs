using FluentValidation;

namespace Taxi.Application.Identity.Auth.Revoke;

/// <summary>
/// Règles de validation de <see cref="RevokeTokenCommand"/>.
/// </summary>
internal sealed class RevokeTokenCommandValidator : AbstractValidator<RevokeTokenCommand>
{
    public RevokeTokenCommandValidator()
    {
        RuleFor(c => c.RefreshToken).NotEmpty();
    }
}
