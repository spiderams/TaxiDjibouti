using Microsoft.AspNetCore.Identity;
using Taxi.Domain.Identity;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.Login;

/// <summary>
/// Gère <see cref="LoginCommand"/> : vérifie les identifiants, gère le verrouillage et émet les jetons d'auth.
/// </summary>
internal sealed class LoginCommandHandler(
    UserManager<ApplicationUser> userManager,
    AuthTokenIssuer issuer)
    : ICommandHandler<LoginCommand, AuthResponse>
{
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("Auth.InvalidCredentials", "Téléphone ou mot de passe incorrect.");

    public async Task<Result<AuthResponse>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByNameAsync(command.PhoneNumber);
        if (user is null)
            return Result.Failure<AuthResponse>(InvalidCredentials);

        if (await userManager.IsLockedOutAsync(user))
            return Result.Failure<AuthResponse>(
                Error.Unauthorized("Auth.LockedOut", "Compte temporairement verrouillé."));

        if (!await userManager.CheckPasswordAsync(user, command.Password))
        {
            await userManager.AccessFailedAsync(user);
            return Result.Failure<AuthResponse>(InvalidCredentials);
        }

        await userManager.ResetAccessFailedCountAsync(user);
        var roles = await userManager.GetRolesAsync(user);

        var (response, _) = await issuer.IssueAsync(user, roles.ToList(), Guid.NewGuid(), cancellationToken);
        return response;
    }
}
