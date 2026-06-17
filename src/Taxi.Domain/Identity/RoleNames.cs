namespace Taxi.Domain.Identity;

/// <summary>
/// Catalogue des noms de rôles applicatifs reconnus par la plateforme.
/// Centralise les constantes pour éviter les chaînes de caractères dupliquées dans les autorisations et les seeds.
/// </summary>
public static class RoleNames
{
    public const string Client = "Client";
    public const string Driver = "Driver";
    public const string Admin = "Admin";

    public static readonly string[] All = [Client, Driver, Admin];
}
