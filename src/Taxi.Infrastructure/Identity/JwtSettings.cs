namespace Taxi.Infrastructure.Identity;

/// <summary>
/// Paramètres JWT (clé, émetteur, audience, durées).
/// </summary>
internal sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Secret { get; init; } = string.Empty;
    public string Issuer { get; init; } = "TaxiDjibouti";
    public string Audience { get; init; } = "TaxiDjiboutiApp";
    public int AccessTokenLifetimeMinutes { get; init; } = 60;
    public int RefreshTokenLifetimeDays { get; init; } = 7;
}
