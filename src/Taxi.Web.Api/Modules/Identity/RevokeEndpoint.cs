using Taxi.Application.Identity.Auth.Revoke;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Identity;

/// <summary>
/// Endpoints REST du module Identity (révocation d'un refresh token pour déconnexion).
/// </summary>
public sealed class RevokeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/revoke", async (
            RevokeTokenCommand command,
            ICommandHandler<RevokeTokenCommand, bool> handler,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(command, ct);
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        })
        .RequireAuthorization()
        .WithName("Revoke")
        .WithTags(Tags.Identity)
        .WithSummary("Révoquer un refresh token (logout)")
        .WithDescription("Révoque le refresh token fourni pour déconnecter l'appareil.");
    }
}
