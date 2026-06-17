using Taxi.SharedKernel;

namespace Taxi.Application.Realtime;

/// <summary>
/// Catalogue des erreurs métier liées aux opérations temps réel (localisation et accès aux courses).
/// </summary>
public static class RealtimeErrors
{
    public static readonly Error DriverNotFound = Error.NotFound("Realtime.DriverNotFound", "Profil chauffeur introuvable.");
    public static readonly Error RideNotAssigned = Error.Forbidden("Realtime.RideNotAssigned", "Cette course n'est pas assignée à ce chauffeur.");
    public static readonly Error RideNotActive = Error.Conflict("Realtime.RideNotActive", "Cette course est terminée ou annulée.");
}
