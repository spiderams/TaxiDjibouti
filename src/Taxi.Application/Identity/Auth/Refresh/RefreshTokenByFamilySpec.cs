using Ardalis.Specification;
using Taxi.Domain.Identity;

namespace Taxi.Application.Identity.Auth.Refresh;

/// <summary>
/// Spécification : sélectionne tous les refresh tokens appartenant à une même famille de rotation.
/// </summary>
internal sealed class RefreshTokenByFamilySpec : Specification<RefreshToken>
{
    public RefreshTokenByFamilySpec(Guid familyId)
        => Query.Where(t => t.FamilyId == familyId);
}
