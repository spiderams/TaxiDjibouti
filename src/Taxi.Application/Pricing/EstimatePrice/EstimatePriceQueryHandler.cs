using Taxi.Application.Abstractions;
using Taxi.Domain.Pricing;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Pricing.EstimatePrice;

/// <summary>
/// Gère <see cref="EstimatePriceQuery"/> : recherche le tarif zone-à-zone configuré et retourne le prix (ou le tarif par défaut).
/// </summary>
internal sealed class EstimatePriceQueryHandler(IRepository<ZonePrice> repository)
    : IQueryHandler<EstimatePriceQuery, EstimatePriceResponse>
{
    public async Task<Result<EstimatePriceResponse>> Handle(
        EstimatePriceQuery query, CancellationToken cancellationToken)
    {
        var match = await repository.FirstOrDefaultAsync(
            new ZonePriceByZonesSpec(query.FromZone, query.ToZone), cancellationToken);

        var price = match?.Price ?? ZonePrice.DefaultPrice;
        return new EstimatePriceResponse(query.FromZone, query.ToZone, price);
    }
}
