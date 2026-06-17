using Ardalis.Specification;
using Taxi.Domain.Drivers;

namespace Taxi.Application.Drivers;

/// <summary>
/// Spécification : sélectionne le profil chauffeur associé à un identifiant utilisateur Identity.
/// </summary>
internal sealed class DriverByUserIdSpec : Specification<Driver>
{
    public DriverByUserIdSpec(string userId)
        => Query.Where(d => d.UserId == userId);
}
