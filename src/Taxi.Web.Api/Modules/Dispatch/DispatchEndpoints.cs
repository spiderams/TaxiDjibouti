using Taxi.Application.Dispatch;
using Taxi.Application.Dispatch.FindNearestDrivers;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Dispatch;

/// <summary>
/// Endpoints REST du module Dispatch (recherche des chauffeurs disponibles les plus proches).
/// </summary>
public sealed class DispatchEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dispatch/nearest-drivers", async (
            double lat, double lon,
            IQueryHandler<FindNearestDriversQuery, IReadOnlyList<NearbyDriver>> handler,
            CancellationToken ct,
            double? radius = null,
            int? max = null) =>
        {
            var query = new FindNearestDriversQuery(lat, lon, radius ?? 5000, max ?? 10);
            return (await handler.Handle(query, ct)).ToHttpResult();
        })
        .RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin))
        .WithName("NearestDrivers")
        .WithTags(Tags.Dispatch)
        .WithSummary("Chauffeurs disponibles les plus proches")
        .WithDescription("Renvoie les chauffeurs disponibles dans le rayon (m, défaut 5000), triés par distance, limités à max (défaut 10).");
    }
}
