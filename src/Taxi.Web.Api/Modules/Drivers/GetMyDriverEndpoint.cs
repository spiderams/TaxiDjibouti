using System.Security.Claims;
using Taxi.Application.Drivers;
using Taxi.Application.Drivers.GetMyDriver;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Drivers;

/// <summary>
/// Endpoints REST du module Drivers (consultation du profil chauffeur de l'utilisateur courant).
/// </summary>
public sealed class GetMyDriverEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/drivers/me", async (
            ClaimsPrincipal principal,
            IQueryHandler<GetMyDriverQuery, DriverDto> handler,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await handler.Handle(new GetMyDriverQuery(userId), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization(policy => policy.RequireRole(RoleNames.Driver))
        .WithName("GetMyDriver")
        .WithTags(Tags.Drivers)
        .WithSummary("Consulter mon profil chauffeur")
        .WithDescription("Renvoie le profil chauffeur de l'utilisateur courant (404 s'il n'existe pas).");
    }
}
