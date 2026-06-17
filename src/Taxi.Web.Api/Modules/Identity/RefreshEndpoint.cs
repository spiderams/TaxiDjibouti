using Taxi.Application.Identity.Auth;
using Taxi.Application.Identity.Auth.Refresh;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Identity;

/// <summary>
/// Endpoints REST du module Identity (renouvellement des tokens par rotation du refresh token).
/// </summary>
public sealed class RefreshEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/refresh", async (
            RefreshTokenCommand command,
            ICommandHandler<RefreshTokenCommand, AuthResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(command, ct);
            return result.ToHttpResult();
        })
        .AllowAnonymous()
        .WithName("Refresh")
        .WithTags(Tags.Identity)
        .WithSummary("Renouveler les tokens")
        .WithDescription("Échange un refresh token valide contre un nouvel access token + refresh token (rotation).");
    }
}
