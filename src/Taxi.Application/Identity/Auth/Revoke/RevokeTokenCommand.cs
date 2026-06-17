using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.Revoke;

/// <summary>
/// Commande de révocation explicite d'un refresh token lors d'une déconnexion (logout).
/// </summary>
public sealed record RevokeTokenCommand(string RefreshToken) : ICommand<bool>;
