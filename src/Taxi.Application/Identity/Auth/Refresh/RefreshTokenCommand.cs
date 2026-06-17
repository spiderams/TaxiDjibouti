using Taxi.Application.Identity.Auth;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.Refresh;

/// <summary>
/// Commande de renouvellement de session à partir d'un refresh token valide (rotation avec détection de réutilisation).
/// </summary>
public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<AuthResponse>;
