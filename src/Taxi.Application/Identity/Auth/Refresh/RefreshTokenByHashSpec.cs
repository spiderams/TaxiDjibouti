using Ardalis.Specification;
using Taxi.Domain.Identity;

namespace Taxi.Application.Identity.Auth.Refresh;

/// <summary>
/// Spécification : sélectionne un refresh token par son hash pour validation lors du renouvellement.
/// </summary>
internal sealed class RefreshTokenByHashSpec : Specification<RefreshToken>
{
    public RefreshTokenByHashSpec(string tokenHash)
        => Query.Where(t => t.TokenHash == tokenHash);
}
