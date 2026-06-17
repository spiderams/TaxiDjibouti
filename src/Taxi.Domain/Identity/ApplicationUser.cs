using Microsoft.AspNetCore.Identity;

namespace Taxi.Domain.Identity;

/// <summary>
/// Utilisateur de la plateforme TaxiDjibouti : étend IdentityUser d'ASP.NET Core Identity
/// pour y ajouter le nom complet et la date d'inscription, communes à tous les rôles (Client, Chauffeur, Admin).
/// </summary>
public sealed class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
