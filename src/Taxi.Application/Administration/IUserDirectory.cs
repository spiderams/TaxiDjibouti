namespace Taxi.Application.Administration;

/// <summary>
/// Annuaire des utilisateurs de l'application : accès en lecture au référentiel d'identité (ASP.NET Core Identity)
/// sans exposer EF Core à la couche Application.
/// </summary>
public interface IUserDirectory
{
    Task<int> CountAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken);
}
