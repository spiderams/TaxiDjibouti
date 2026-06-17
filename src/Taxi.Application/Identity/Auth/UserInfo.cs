namespace Taxi.Application.Identity.Auth;

/// <summary>
/// Informations publiques de l'utilisateur authentifié : identifiant, nom complet, téléphone et rôles.
/// </summary>
public sealed record UserInfo(string Id, string FullName, string PhoneNumber, IReadOnlyList<string> Roles);
