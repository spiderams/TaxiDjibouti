using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Taxi.Application.Abstractions;
using Taxi.Application.Identity.Abstractions;
using Taxi.Application.Identity.Auth;
using Taxi.Domain.Identity;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.Refresh;

/// <summary>
/// Gère <see cref="RefreshTokenCommand"/> : valide le token, détecte la réutilisation, effectue la rotation et émet de nouveaux jetons.
/// </summary>
internal sealed partial class RefreshTokenCommandHandler(
    IRepository<RefreshToken> refreshTokens,
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    AuthTokenIssuer issuer,
    ILogger<RefreshTokenCommandHandler> logger)
    : ICommandHandler<RefreshTokenCommand, AuthResponse>
{
    private static readonly Error Invalid = Error.Unauthorized("Auth.InvalidToken", "Refresh token invalide.");

    public async Task<Result<AuthResponse>> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        var hash = tokenService.HashRefreshToken(command.RefreshToken);
        var stored = await refreshTokens.FirstOrDefaultAsync(new RefreshTokenByHashSpec(hash), cancellationToken);

        if (stored is null)
            return Result.Failure<AuthResponse>(Invalid);

        if (stored.IsRevoked)
        {
            // Reuse of an already-rotated token → revoke the whole family.
            var family = await refreshTokens.ListAsync(new RefreshTokenByFamilySpec(stored.FamilyId), cancellationToken);
            foreach (var token in family)
                token.Revoke("TokenReuse");
            LogTokenReuseDetected(logger, stored.FamilyId);
            await refreshTokens.SaveChangesAsync(cancellationToken);
            return Result.Failure<AuthResponse>(Error.Unauthorized("Auth.TokenReuse", "Réutilisation de token détectée."));
        }

        if (stored.ExpiresAt <= DateTime.UtcNow)
            return Result.Failure<AuthResponse>(Error.Unauthorized("Auth.ExpiredToken", "Refresh token expiré."));

        var user = await userManager.FindByIdAsync(stored.UserId);
        if (user is null)
            return Result.Failure<AuthResponse>(Invalid);

        var roles = await userManager.GetRolesAsync(user);
        var (response, newToken) = await issuer.IssueAsync(user, roles.ToList(), stored.FamilyId, cancellationToken);

        stored.Revoke("Rotation", newToken.Id);
        await refreshTokens.UpdateAsync(stored, cancellationToken);

        return response;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Réutilisation de refresh token détectée → révocation de toute la famille {FamilyId}")]
    private static partial void LogTokenReuseDetected(ILogger logger, Guid familyId);
}
