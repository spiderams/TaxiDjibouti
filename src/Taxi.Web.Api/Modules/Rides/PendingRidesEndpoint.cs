using Taxi.Application.Rides;
using Taxi.Application.Rides.Pending;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Rides;

/// <summary>
/// Endpoints REST du module Rides (liste des courses en attente d'attribution).
/// </summary>
public sealed class PendingRidesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/rides/pending", async (
            IQueryHandler<GetPendingRidesQuery, IReadOnlyList<RideDto>> handler, CancellationToken ct) =>
        {
            var result = await handler.Handle(new GetPendingRidesQuery(), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization(p => p.RequireRole(RoleNames.Driver))
        .WithName("PendingRides").WithTags(Tags.Rides)
        .WithSummary("Courses en attente").WithDescription("Liste des courses en attente d'un chauffeur.");
    }
}
