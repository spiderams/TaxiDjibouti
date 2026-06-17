using Microsoft.AspNetCore.Identity;
using Taxi.Domain.Identity;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.GetMe;

/// <summary>
/// Gère <see cref="GetMeQuery"/> : charge l'utilisateur depuis Identity et retourne ses informations publiques.
/// </summary>
internal sealed class GetMeQueryHandler(UserManager<ApplicationUser> userManager)
    : IQueryHandler<GetMeQuery, UserInfo>
{
    public async Task<Result<UserInfo>> Handle(GetMeQuery query, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(query.UserId);
        if (user is null)
            return Result.Failure<UserInfo>(Error.NotFound("Auth.UserNotFound", "Utilisateur introuvable."));

        var roles = await userManager.GetRolesAsync(user);
        return new UserInfo(user.Id, user.FullName, user.PhoneNumber!, roles.ToList());
    }
}
