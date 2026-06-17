using System.Security.Claims;
using Taxi.Application.Rides;
using Taxi.Application.Rides.Rate;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Rides;

public sealed record RateRideRequest(int Score, string? Comment);

/// <summary>
/// Endpoints REST du module Rides (notation d'une course terminée par le client).
/// </summary>
public sealed class RateRideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/rides/{id:int}/rate", async (
            int id, RateRideRequest body, ClaimsPrincipal principal,
            ICommandHandler<RateRideCommand, RatingDto> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            var result = await handler.Handle(new RateRideCommand(id, userId, body.Score, body.Comment), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization(p => p.RequireRole(RoleNames.Client))
        .WithName("RateRide").WithTags(Tags.Rides)
        .WithSummary("Noter une course").WithDescription("Note (1-5) une course terminée ; met à jour la moyenne du chauffeur.");
    }
}
