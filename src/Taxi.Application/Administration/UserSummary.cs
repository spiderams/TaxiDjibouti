namespace Taxi.Application.Administration;

/// <summary>
/// Résumé d'un compte utilisateur destiné à l'administration : identité, coordonnées et rôles assignés.
/// </summary>
public sealed record UserSummary(string Id, string FullName, string PhoneNumber, IReadOnlyList<string> Roles);
