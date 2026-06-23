namespace Taxi.Application.Realtime;

/// <summary>
/// Notifications temps réel (SignalR) émises par la couche Application sans en connaître l'implémentation : changement de statut de course, nouvelle course en attente, offre ciblée à un chauffeur.
/// </summary>
public interface IRealtimeNotifier
{
    Task RideStatusChangedAsync(int rideId, string clientId, int? driverId, string status, CancellationToken cancellationToken);
    Task NewPendingRideAsync(int rideId, CancellationToken cancellationToken);
    Task RideOfferedAsync(string driverUserId, int rideId, DateTime expiresAt, CancellationToken cancellationToken);

    /// <summary>
    /// Notifie un chauffeur que l'offre de course ne lui est plus proposée (course prise, vague expirée ou course annulée),
    /// afin que son écran retire la carte d'offre. La <paramref name="reason"/> vaut "taken", "expired" ou "cancelled".
    /// </summary>
    Task RideOfferRevokedAsync(string driverUserId, int rideId, string reason, CancellationToken cancellationToken);
}
