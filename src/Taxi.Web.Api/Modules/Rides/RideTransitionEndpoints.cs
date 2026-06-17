using System.Security.Claims;
using Taxi.Application.Rides;
using Taxi.Application.Rides.Transitions;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Rides;

/// <summary>
/// Endpoints REST du module Rides (transitions de statut de course : arrivé, démarré, terminé).
/// </summary>
public sealed class RideTransitionEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rides/{id:int}")
            .RequireAuthorization(p => p.RequireRole(RoleNames.Driver))
            .WithTags(Tags.Rides);

        group.MapPost("/arrived", async (int id, ClaimsPrincipal principal,
            ICommandHandler<MarkArrivedCommand, RideDto> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            return (await handler.Handle(new MarkArrivedCommand(id, userId), ct)).ToHttpResult();
        }).WithName("RideArrived").WithSummary("Chauffeur arrivé");

        group.MapPost("/start", async (int id, ClaimsPrincipal principal,
            ICommandHandler<StartRideCommand, RideDto> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            return (await handler.Handle(new StartRideCommand(id, userId), ct)).ToHttpResult();
        }).WithName("RideStart").WithSummary("Démarrer la course");

        group.MapPost("/complete", async (int id, ClaimsPrincipal principal,
            ICommandHandler<CompleteRideCommand, RideDto> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            return (await handler.Handle(new CompleteRideCommand(id, userId), ct)).ToHttpResult();
        }).WithName("RideComplete").WithSummary("Terminer la course");
    }
}
