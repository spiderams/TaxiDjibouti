using Ardalis.Specification;
using Taxi.Domain.Drivers;

namespace Taxi.Application.Drivers;

/// <summary>
/// Spécification : sélectionne un chauffeur par son identifiant technique.
/// </summary>
internal sealed class DriverByIdSpec : Specification<Driver>
{
    public DriverByIdSpec(int id) => Query.Where(d => d.Id == id);
}
