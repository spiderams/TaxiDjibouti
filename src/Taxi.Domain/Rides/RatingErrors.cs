using Taxi.SharedKernel;

namespace Taxi.Domain.Rides;

/// <summary>
/// Catalogue des erreurs métier liées aux évaluations (Rating) : regroupe les codes d'erreur
/// retournés lorsque les règles de gestion empêchent la création ou la modification d'une évaluation.
/// </summary>
public static class RatingErrors
{
    public static readonly Error RideNotCompleted = Error.Conflict("Rating.RideNotCompleted", "On ne peut noter qu'une course terminée.");
    public static readonly Error NoDriver = Error.Conflict("Rating.NoDriver", "Aucun chauffeur associé à cette course.");
}
