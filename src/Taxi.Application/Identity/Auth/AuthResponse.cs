namespace Taxi.Application.Identity.Auth;

/// <summary>
/// Réponse d'authentification contenant les jetons d'accès et refresh ainsi que les informations de l'utilisateur.
/// </summary>
public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiresAt,
    string TokenType,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserInfo User);
