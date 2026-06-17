using Taxi.Domain.Identity;

namespace Taxi.Application.Identity.Abstractions;

/// <summary>
/// Abstraction responsable de la création et du hachage des jetons JWT et refresh.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Crée un jeton d'accès JWT signé pour l'utilisateur avec ses rôles.
    /// </summary>
    /// <param name="user">Utilisateur concerné.</param>
    /// <param name="roles">Liste des rôles de l'utilisateur.</param>
    /// <returns>Jeton d'accès avec date d'expiration.</returns>
    AccessToken CreateAccessToken(ApplicationUser user, IEnumerable<string> roles);

    /// <summary>
    /// Génère une valeur de refresh token opaque avec son hash et sa date d'expiration.
    /// </summary>
    /// <returns>Valeur brute, hash et expiration du refresh token.</returns>
    RefreshTokenValue CreateRefreshToken();

    /// <summary>
    /// Calcule le hash sécurisé d'un refresh token brut pour stockage.
    /// </summary>
    /// <param name="rawToken">Valeur brute du refresh token.</param>
    /// <returns>Hash du token.</returns>
    string HashRefreshToken(string rawToken);
}
