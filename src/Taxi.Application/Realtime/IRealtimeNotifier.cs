namespace Taxi.Application.Realtime;

/// <summary>
/// Notifications temps réel (SignalR) émises par la couche Application sans en connaître l'implémentation : changement de statut de course, nouvelle course en attente, offre ciblée à un chauffeur.
/// </summary>
public interface IRealtimeNotifier
{
    Task RideStatusChangedAsync(int rideId, string clientId, int? driverId, string status, CancellationToken cancellationToken);
    Task NewPendingRideAsync(int rideId, CancellationToken cancellationToken);
    Task RideOfferedAsync(string driverUserId, int rideId, DateTime expiresAt, CancellationToken cancellationToken);
}
