using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Taxi.Application.Administration;
using Taxi.Domain.Identity;

namespace Taxi.Infrastructure.Identity;

/// <summary>
/// Lecture des utilisateurs via UserManager pour le back-office admin.
/// </summary>
internal sealed class UserDirectory(UserManager<ApplicationUser> userManager) : IUserDirectory
{
    public Task<int> CountAsync(CancellationToken cancellationToken)
        => userManager.Users.CountAsync(cancellationToken);

    public async Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var users = await userManager.Users.ToListAsync(cancellationToken);
        var summaries = new List<UserSummary>(users.Count);

        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            summaries.Add(new UserSummary(user.Id, user.FullName, user.PhoneNumber ?? string.Empty, roles.ToList()));
        }

        return summaries;
    }
}
