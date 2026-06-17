using Taxi.Application.Abstractions;
using Taxi.Application.Identity.Abstractions;
using Taxi.Application.Identity.Auth.Refresh;
using Taxi.Domain.Identity;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.Revoke;

/// <summary>
/// Gère <see cref="RevokeTokenCommand"/> : invalide le refresh token identifié par son hash lors du logout.
/// </summary>
internal sealed class RevokeTokenCommandHandler(
    IRepository<RefreshToken> refreshTokens,
    ITokenService tokenService)
    : ICommandHandler<RevokeTokenCommand, bool>
{
    public async Task<Result<bool>> Handle(RevokeTokenCommand command, CancellationToken cancellationToken)
    {
        var hash = tokenService.HashRefreshToken(command.RefreshToken);
        var stored = await refreshTokens.FirstOrDefaultAsync(new RefreshTokenByHashSpec(hash), cancellationToken);

        if (stored is not null && !stored.IsRevoked)
        {
            stored.Revoke("Logout");
            await refreshTokens.UpdateAsync(stored, cancellationToken);
        }

        return true;
    }
}
