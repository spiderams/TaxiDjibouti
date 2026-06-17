using System.Security.Claims;
using Taxi.Application.Rides;
using Taxi.Application.Rides.MyRides;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Rides;

/// <summary>
/// Endpoints REST du module Rides (liste des courses de l'utilisateur courant, client ou chauffeur).
/// </summary>
public sealed class MyRidesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/rides/my-rides", async (
            ClaimsPrincipal principal,
            IQueryHandler<GetMyRidesQuery, IReadOnlyList<RideDto>> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            var asDriver = principal.IsInRole(RoleNames.Driver);
            var result = await handler.Handle(new GetMyRidesQuery(userId, asDriver), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization()
        .WithName("MyRides").WithTags(Tags.Rides)
        .WithSummary("Mes courses").WithDescription("Client : ses courses ; Chauffeur : les courses qui lui sont assignées.");
    }
}
