using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Taxi.Application.Identity.Auth;
using Taxi.Application.Identity.Auth.GetMe;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Identity;

/// <summary>
/// Endpoints REST du module Identity (consultation du profil de l'utilisateur courant).
/// </summary>
public sealed class MeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/me", async (
            ClaimsPrincipal principal,
            IQueryHandler<GetMeQuery, UserInfo> handler,
            CancellationToken ct) =>
        {
            var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                         ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await handler.Handle(new GetMeQuery(userId), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization()
        .WithName("GetMe")
        .WithTags(Tags.Identity)
        .WithSummary("Profil de l'utilisateur courant")
        .WithDescription("Renvoie les informations de l'utilisateur authentifié (à partir du JWT).");
    }
}
