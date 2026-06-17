using Taxi.Application.Identity.Auth;
using Taxi.Application.Identity.Auth.Register;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Identity;

/// <summary>
/// Endpoints REST du module Identity (inscription d'un nouvel utilisateur).
/// </summary>
public sealed class RegisterEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/register", async (
            RegisterCommand command,
            ICommandHandler<RegisterCommand, AuthResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(command, ct);
            return result.ToHttpResult();
        })
        .AllowAnonymous()
        .WithName("Register")
        .WithTags(Tags.Identity)
        .WithSummary("Inscrire un nouvel utilisateur")
        .WithDescription("Crée un compte (Client, Driver ou Admin) avec login par téléphone et renvoie un JWT.");
    }
}
