namespace Taxi.Application.Identity.Abstractions;

/// <summary>
/// Regroupe la valeur brute, le hash et la date d'expiration d'un refresh token nouvellement créé.
/// </summary>
public sealed record RefreshTokenValue(string RawToken, string TokenHash, DateTime ExpiresAt);
