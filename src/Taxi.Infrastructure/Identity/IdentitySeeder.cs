using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Domain.Identity;

namespace Taxi.Infrastructure.Identity;

/// <summary>
/// Crée les rôles par défaut au démarrage.
/// </summary>
public static class IdentitySeeder
{
    public static async Task SeedRolesAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in RoleNames.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }
}
