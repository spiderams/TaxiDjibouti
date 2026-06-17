using Taxi.SharedKernel;

namespace Taxi.Domain.Rides;

/// <summary>
/// Catalogue des erreurs métier liées aux courses (Ride) : regroupe les codes d'erreur
/// retournés lorsque les règles de gestion empêchent une transition d'état ou une opération sur une course.
/// </summary>
public static class RideErrors
{
    public static readonly Error NotFound = Error.NotFound("Ride.NotFound", "Course introuvable.");
    public static readonly Error NotPending = Error.Conflict("Ride.NotPending", "Cette course n'est plus disponible.");
    public static readonly Error InvalidTransition = Error.Conflict("Ride.InvalidTransition", "Transition de statut invalide.");
    public static readonly Error CannotCancel = Error.Conflict("Ride.CannotCancel", "Cette course ne peut plus être annulée.");
    public static readonly Error DriverNotAvailable = Error.Conflict("Ride.DriverNotAvailable", "Le chauffeur doit être disponible.");
    public static readonly Error NotAssignedDriver = Error.Forbidden("Ride.NotAssignedDriver", "Cette course n'est pas assignée à ce chauffeur.");
    public static readonly Error NoDriverProfile = Error.NotFound("Ride.NoDriverProfile", "Profil chauffeur introuvable.");
    public static readonly Error NotOffered = Error.Conflict("Ride.NotOffered", "Cette course n'est pas en cours d'offre.");
    public static readonly Error OfferMismatch = Error.Forbidden("Ride.OfferMismatch", "Cette offre ne vous concerne pas.");
    public static readonly Error OfferExpired = Error.Conflict("Ride.OfferExpired", "Cette offre a expiré.");
}
