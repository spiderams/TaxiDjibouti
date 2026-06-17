using System.Security.Claims;
using Taxi.Application.Rides;
using Taxi.Application.Rides.Reporting;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Rides;

public sealed record ReportRideRequest(string Reason, string? Description);

/// <summary>
/// Endpoints REST du module Rides (signalement d'une course par le client).
/// </summary>
public sealed class ReportRideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/rides/{id:int}/report", async (
            int id, ReportRideRequest body, ClaimsPrincipal principal,
            ICommandHandler<ReportRideCommand, ReportDto> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            var result = await handler.Handle(new ReportRideCommand(id, userId, body.Reason, body.Description), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization(p => p.RequireRole(RoleNames.Client))
        .WithName("ReportRide").WithTags(Tags.Rides)
        .WithSummary("Signaler une course").WithDescription("Signale une course (raison + description).");
    }
}
