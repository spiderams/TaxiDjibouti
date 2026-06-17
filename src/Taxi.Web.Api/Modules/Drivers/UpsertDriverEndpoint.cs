using System.Security.Claims;
using Taxi.Application.Drivers;
using Taxi.Application.Drivers.UpsertProfile;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Drivers;

public sealed record UpsertDriverRequest(string LicenseNumber, string VehiclePlate, string VehicleType);

/// <summary>
/// Endpoints REST du module Drivers (création ou mise à jour du profil chauffeur).
/// </summary>
public sealed class UpsertDriverEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/drivers", async (
            UpsertDriverRequest body,
            ClaimsPrincipal principal,
            ICommandHandler<UpsertDriverProfileCommand, DriverDto> handler,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await handler.Handle(
                new UpsertDriverProfileCommand(userId, body.LicenseNumber, body.VehiclePlate, body.VehicleType), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization(policy => policy.RequireRole(RoleNames.Driver))
        .WithName("UpsertDriverProfile")
        .WithTags(Tags.Drivers)
        .WithSummary("Créer ou mettre à jour mon profil chauffeur")
        .WithDescription("Self-service : crée le profil s'il n'existe pas pour l'utilisateur courant, sinon le met à jour.");
    }
}
