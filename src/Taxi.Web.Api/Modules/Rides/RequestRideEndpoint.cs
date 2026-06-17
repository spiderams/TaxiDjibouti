using System.Security.Claims;
using Taxi.Application.Rides;
using Taxi.Application.Rides.Request;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Rides;

public sealed record RequestRideRequest(
    string PickupAddress, string DestinationAddress,
    string PickupZone, string DestinationZone,
    double? PickupLatitude, double? PickupLongitude,
    double? DestinationLatitude, double? DestinationLongitude);

/// <summary>
/// Endpoints REST du module Rides (demande de course par le client avec adresses et zones de départ/arrivée).
/// </summary>
public sealed class RequestRideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/rides/request", async (
            RequestRideRequest body, ClaimsPrincipal principal,
            ICommandHandler<RequestRideCommand, RideDto> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            var result = await handler.Handle(new RequestRideCommand(
                userId, body.PickupAddress, body.DestinationAddress, body.PickupZone, body.DestinationZone,
                body.PickupLatitude, body.PickupLongitude, body.DestinationLatitude, body.DestinationLongitude), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization(p => p.RequireRole(RoleNames.Client))
        .WithName("RequestRide").WithTags(Tags.Rides)
        .WithSummary("Demander une course").WithDescription("Crée une course en attente avec un prix estimé.");
    }
}
