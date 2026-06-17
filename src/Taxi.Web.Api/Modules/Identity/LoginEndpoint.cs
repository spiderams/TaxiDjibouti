using Taxi.Application.Identity.Auth;
using Taxi.Application.Identity.Auth.Login;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Identity;

/// <summary>
/// Endpoints REST du module Identity (authentification par téléphone et mot de passe).
/// </summary>
public sealed class LoginEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", async (
            LoginCommand command,
            ICommandHandler<LoginCommand, AuthResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(command, ct);
            return result.ToHttpResult();
        })
        .AllowAnonymous()
        .WithName("Login")
        .WithTags(Tags.Identity)
        .WithSummary("Authentifier un utilisateur")
        .WithDescription("Vérifie le téléphone et le mot de passe, puis renvoie un JWT.");
    }
}
