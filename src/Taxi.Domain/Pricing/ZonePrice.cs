using Taxi.SharedKernel;

namespace Taxi.Domain.Pricing;

/// <summary>
/// Tarif applicable entre deux zones géographiques de la ville de Djibouti.
/// Sert de barème de référence pour estimer le prix d'une course avant sa confirmation par le client.
/// Un prix par défaut (<see cref="DefaultPrice"/>) est appliqué lorsqu'aucune entrée ne correspond à la paire de zones.
/// </summary>
public sealed class ZonePrice : Entity
{
    public const decimal DefaultPrice = 1000m;

    public string FromZone { get; private set; } = string.Empty;
    public string ToZone { get; private set; } = string.Empty;
    public decimal Price { get; private set; }

    private ZonePrice() { } // EF

    /// <summary>
    /// Crée un nouveau tarif zonal entre une zone de départ et une zone d'arrivée.
    /// </summary>
    public static ZonePrice Create(string fromZone, string toZone, decimal price)
        => new() { FromZone = fromZone, ToZone = toZone, Price = price };
}
