using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Pricing.EstimatePrice;

/// <summary>
/// Requête d'estimation du tarif entre deux zones géographiques de Djibouti.
/// </summary>
public sealed record EstimatePriceQuery(string FromZone, string ToZone)
    : IQuery<EstimatePriceResponse>;
