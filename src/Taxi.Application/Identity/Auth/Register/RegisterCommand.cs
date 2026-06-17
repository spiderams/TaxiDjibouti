using Taxi.Application.Identity.Auth;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.Register;

/// <summary>
/// Commande d'inscription d'un nouvel utilisateur (nom complet, téléphone, mot de passe, rôle).
/// </summary>
public sealed record RegisterCommand(string FullName, string PhoneNumber, string Password, string Role)
    : ICommand<AuthResponse>;
