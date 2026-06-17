using System.Security.Claims;
using Taxi.Application.Rides;
using Taxi.Application.Rides.Accept;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Rides;

/// <summary>
/// Endpoints REST du module Rides (acceptation d'une course en attente par le chauffeur).
/// </summary>
public sealed class AcceptRideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/rides/{id:int}/accept", async (
            int id, ClaimsPrincipal principal,
            ICommandHandler<AcceptRideCommand, RideDto> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            var result = await handler.Handle(new AcceptRideCommand(id, userId), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization(p => p.RequireRole(RoleNames.Driver))
        .WithName("AcceptRide").WithTags(Tags.Rides)
        .WithSummary("Accepter une course").WithDescription("Le chauffeur disponible accepte une course en attente.");
    }
}
