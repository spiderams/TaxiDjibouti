using Taxi.Application.Identity.Auth;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.Login;

/// <summary>
/// Commande d'authentification d'un utilisateur par numéro de téléphone et mot de passe.
/// </summary>
public sealed record LoginCommand(string PhoneNumber, string Password) : ICommand<AuthResponse>;
