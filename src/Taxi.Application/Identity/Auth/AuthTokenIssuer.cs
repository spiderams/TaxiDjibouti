using Taxi.Application.Abstractions;
using Taxi.Application.Identity.Abstractions;
using Taxi.Domain.Identity;

namespace Taxi.Application.Identity.Auth;

/// <summary>
/// Service interne qui orchestre la création des jetons d'accès et refresh, et persiste l'entité RefreshToken.
/// </summary>
internal sealed class AuthTokenIssuer(
    ITokenService tokenService,
    IRepository<RefreshToken> refreshTokens)
{
    /// <summary>
    /// Émet une paire de jetons (accès + refresh), persiste le refresh token et retourne la réponse d'auth.
    /// </summary>
    /// <param name="user">Utilisateur pour lequel émettre les jetons.</param>
    /// <param name="roles">Rôles de l'utilisateur à inclure dans le JWT.</param>
    /// <param name="familyId">Identifiant de la famille de rotation du refresh token.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>Réponse d'auth et entité refresh token persistée.</returns>
    public async Task<(AuthResponse Response, RefreshToken Token)> IssueAsync(
        ApplicationUser user, IReadOnlyList<string> roles, Guid familyId, CancellationToken cancellationToken)
    {
        var access = tokenService.CreateAccessToken(user, roles);
        var refresh = tokenService.CreateRefreshToken();

        var entity = RefreshToken.Create(user.Id, refresh.TokenHash, refresh.ExpiresAt, familyId);
        await refreshTokens.AddAsync(entity, cancellationToken); // Ardalis AddAsync persists and sets Id

        var response = new AuthResponse(
            access.Token, access.ExpiresAt, "Bearer",
            refresh.RawToken, refresh.ExpiresAt,
            new UserInfo(user.Id, user.FullName, user.PhoneNumber!, roles));

        return (response, entity);
    }
}
