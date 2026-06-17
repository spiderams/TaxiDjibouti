using Ardalis.Specification;
using Taxi.Domain.Pricing;

namespace Taxi.Application.Pricing.EstimatePrice;

/// <summary>
/// Spécification : sélectionne le tarif correspondant à une paire de zones (départ → arrivée).
/// </summary>
internal sealed class ZonePriceByZonesSpec : Specification<ZonePrice>
{
    public ZonePriceByZonesSpec(string fromZone, string toZone)
        => Query.Where(z => z.FromZone == fromZone && z.ToZone == toZone);
}
