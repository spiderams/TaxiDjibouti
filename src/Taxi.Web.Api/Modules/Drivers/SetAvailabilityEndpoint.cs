using System.Security.Claims;
using Taxi.Application.Drivers;
using Taxi.Application.Drivers.SetAvailability;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Drivers;

public sealed record SetAvailabilityRequest(bool IsAvailable);

/// <summary>
/// Endpoints REST du module Drivers (basculement de la disponibilité du chauffeur).
/// </summary>
public sealed class SetAvailabilityEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/drivers/set-availability", async (
            SetAvailabilityRequest body,
            ClaimsPrincipal principal,
            ICommandHandler<SetAvailabilityCommand, DriverDto> handler,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await handler.Handle(new SetAvailabilityCommand(userId, body.IsAvailable), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization(policy => policy.RequireRole(RoleNames.Driver))
        .WithName("SetDriverAvailability")
        .WithTags(Tags.Drivers)
        .WithSummary("Définir ma disponibilité")
        .WithDescription("Bascule la disponibilité du chauffeur courant.");
    }
}
