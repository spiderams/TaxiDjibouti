namespace Taxi.Application.Identity.Abstractions;

/// <summary>
/// Représente un jeton d'accès JWT émis avec sa date d'expiration.
/// </summary>
public sealed record AccessToken(string Token, DateTime ExpiresAt);
