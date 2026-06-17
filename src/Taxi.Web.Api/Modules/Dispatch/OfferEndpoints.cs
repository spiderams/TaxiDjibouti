using System.Security.Claims;
using Taxi.Application.Dispatch.AcceptOffer;
using Taxi.Application.Dispatch.DeclineOffer;
using Taxi.Application.Rides;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Dispatch;

/// <summary>
/// Endpoints REST du module Dispatch (acceptation et refus d'une offre de course par le chauffeur).
/// </summary>
public sealed class OfferEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rides/{id:int}")
            .RequireAuthorization(p => p.RequireRole(RoleNames.Driver))
            .WithTags(Tags.Dispatch);

        group.MapPost("/accept-offer", async (int id, ClaimsPrincipal principal,
            ICommandHandler<AcceptOfferCommand, RideDto> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            return (await handler.Handle(new AcceptOfferCommand(id, userId), ct)).ToHttpResult();
        }).WithName("AcceptOffer").WithSummary("Accepter l'offre de course");

        group.MapPost("/decline-offer", async (int id, ClaimsPrincipal principal,
            ICommandHandler<DeclineOfferCommand, RideDto> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            return (await handler.Handle(new DeclineOfferCommand(id, userId), ct)).ToHttpResult();
        }).WithName("DeclineOffer").WithSummary("Refuser l'offre de course");
    }
}
