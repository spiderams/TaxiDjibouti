using Ardalis.Specification;
using Taxi.Domain.Drivers;

namespace Taxi.Application.Drivers;

/// <summary>
/// Spécification : sélectionne les chauffeurs dont l'identifiant figure dans la liste fournie.
/// </summary>
public sealed class DriversByIdsSpec : Specification<Driver>
{
    public DriversByIdsSpec(IEnumerable<int> ids) => Query.Where(d => ids.Contains(d.Id));
}
