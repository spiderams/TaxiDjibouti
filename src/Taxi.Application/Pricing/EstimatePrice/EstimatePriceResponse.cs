namespace Taxi.Application.Pricing.EstimatePrice;

/// <summary>
/// Résultat de l'estimation tarifaire : zones de départ/arrivée et prix calculé en francs djiboutiens.
/// </summary>
public sealed record EstimatePriceResponse(string FromZone, string ToZone, decimal Price);
