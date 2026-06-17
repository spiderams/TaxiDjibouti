using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Taxi.Application.Identity.Auth;
using Taxi.Domain.Identity;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.Register;

/// <summary>
/// Gère <see cref="RegisterCommand"/> : crée l'utilisateur, attribue le rôle, émet les jetons d'accès/refresh.
/// </summary>
internal sealed partial class RegisterCommandHandler(
    UserManager<ApplicationUser> userManager,
    AuthTokenIssuer issuer,
    ILogger<RegisterCommandHandler> logger)
    : ICommandHandler<RegisterCommand, AuthResponse>
{
    public async Task<Result<AuthResponse>> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        var existing = await userManager.FindByNameAsync(command.PhoneNumber);
        if (existing is not null)
            return Result.Failure<AuthResponse>(Error.Conflict("Auth.PhoneTaken", "Ce numéro est déjà utilisé."));

        var user = new ApplicationUser
        {
            UserName = command.PhoneNumber,
            PhoneNumber = command.PhoneNumber,
            FullName = command.FullName
        };

        var created = await userManager.CreateAsync(user, command.Password);
        if (!created.Succeeded)
        {
            var first = created.Errors.FirstOrDefault();
            return Result.Failure<AuthResponse>(
                Error.Validation("Auth.RegisterFailed", first?.Description ?? "Inscription impossible."));
        }

        await userManager.AddToRoleAsync(user, command.Role);
        LogUserRegistered(logger, user.Id, command.Role);
        var roles = await userManager.GetRolesAsync(user);

        var (response, _) = await issuer.IssueAsync(user, roles.ToList(), Guid.NewGuid(), cancellationToken);
        return response;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Utilisateur {UserId} inscrit avec le rôle {Role}")]
    private static partial void LogUserRegistered(ILogger logger, string userId, string role);
}
