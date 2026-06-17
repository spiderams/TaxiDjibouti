using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Taxi.Web.Api.Endpoints;

/// <summary>
/// Extensions pour extraire l'identité (userId) depuis le ClaimsPrincipal.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static string? GetUserId(this ClaimsPrincipal principal)
        => principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
           ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
}
