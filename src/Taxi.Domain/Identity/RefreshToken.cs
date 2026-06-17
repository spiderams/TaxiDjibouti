using Taxi.SharedKernel;

namespace Taxi.Domain.Identity;

/// <summary>
/// Jeton de renouvellement (refresh token) associé à un utilisateur : permet de délivrer de nouveaux JWT
/// sans redemander les identifiants. Supporte la rotation par famille (FamilyId) pour détecter la réutilisation
/// frauduleuse et révoquer automatiquement toute la lignée de tokens.
/// </summary>
public sealed class RefreshToken : Entity
{
    public string UserId { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime ExpiresAt { get; private set; }
    public bool IsRevoked { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }
    public Guid FamilyId { get; private set; }
    public int? ReplacedByTokenId { get; private set; }

    private RefreshToken() { } // EF

    /// <summary>
    /// Crée un nouveau refresh token pour un utilisateur, avec sa date d'expiration et l'identifiant de famille de rotation.
    /// </summary>
    public static RefreshToken Create(string userId, string tokenHash, DateTime expiresAt, Guid familyId)
        => new()
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            FamilyId = familyId
        };

    public bool IsActive => !IsRevoked && ExpiresAt > DateTime.UtcNow;

    /// <summary>
    /// Révoque ce token (déconnexion, rotation ou détection d'abus) en enregistrant la raison
    /// et l'éventuel token successeur. Une révocation déjà effectuée est ignorée (idempotent).
    /// </summary>
    public void Revoke(string reason, int? replacedByTokenId = null)
    {
        if (IsRevoked)
        {
            return;
        }

        IsRevoked = true;
        RevokedAt = DateTime.UtcNow;
        RevokedReason = reason;
        ReplacedByTokenId = replacedByTokenId;
    }
}
