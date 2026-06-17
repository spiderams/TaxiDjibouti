using Microsoft.AspNetCore.SignalR;
using Taxi.Application.Realtime;

namespace Taxi.Web.Api.Realtime;

/// <summary>
/// Implémentation SignalR de <see cref="Taxi.Application.Realtime.IRealtimeNotifier"/> : pousse les événements aux groupes du hub.
/// </summary>
internal sealed class SignalRRealtimeNotifier(
    IHubContext<RideHub> hub,
    ILogger<SignalRRealtimeNotifier> logger) : IRealtimeNotifier
{
    public async Task RideStatusChangedAsync(
        int rideId, string clientId, int? driverId, string status, CancellationToken cancellationToken)
    {
        try
        {
            var payload = new { rideId, status, driverId };
            await hub.Clients.Group($"Client_{clientId}").SendAsync("rideStatusChanged", payload, cancellationToken);
            await hub.Clients.Group($"Ride_{rideId}").SendAsync("rideStatusChanged", payload, cancellationToken);
            await hub.Clients.Group("Admins").SendAsync("rideStatusChanged", payload, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Realtime notify (rideStatusChanged) failed for ride {RideId}", rideId);
        }
    }

    public async Task NewPendingRideAsync(int rideId, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Group("Drivers").SendAsync("newPendingRide", new { rideId }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Realtime notify (newPendingRide) failed for ride {RideId}", rideId);
        }
    }

    public async Task RideOfferedAsync(string driverUserId, int rideId, DateTime expiresAt, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Group($"DriverUser_{driverUserId}")
                .SendAsync("rideOffered", new { rideId, expiresAt }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Realtime notify (rideOffered) failed for ride {RideId}", rideId);
        }
    }
}
