using System.Security.Claims;
using Taxi.Application.Rides;
using Taxi.Application.Rides.Cancel;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Rides;

/// <summary>
/// Endpoints REST du module Rides (annulation d'une course par le client ou le chauffeur assigné).
/// </summary>
public sealed class CancelRideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/rides/{id:int}/cancel", async (
            int id, ClaimsPrincipal principal,
            ICommandHandler<CancelRideCommand, RideDto> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            var isDriver = principal.IsInRole(RoleNames.Driver);
            var result = await handler.Handle(new CancelRideCommand(id, userId, isDriver), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization()
        .WithName("CancelRide").WithTags(Tags.Rides)
        .WithSummary("Annuler une course").WithDescription("Client ou chauffeur assigné, selon la règle d'annulation.");
    }
}
