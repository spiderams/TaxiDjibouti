using Taxi.Application.Pricing.EstimatePrice;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Pricing;

/// <summary>
/// Endpoints REST du module Pricing (estimation du prix d'une course entre deux zones).
/// </summary>
public sealed class EstimatePriceEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pricing/estimate", async (
            string fromZone, string toZone,
            IQueryHandler<EstimatePriceQuery, EstimatePriceResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(new EstimatePriceQuery(fromZone, toZone), ct);
            return result.ToHttpResult();
        })
        .WithName("EstimatePrice")
        .WithTags(Tags.Pricing)
        .WithSummary("Estimer le prix d'une course")
        .WithDescription("Renvoie le prix estimé entre deux zones (prix par défaut si aucun tarif n'est défini).");
    }
}
