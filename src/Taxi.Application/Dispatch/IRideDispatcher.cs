namespace Taxi.Application.Dispatch;

/// <summary>
/// Contrat du service de dispatch automatique : propose une course au prochain chauffeur disponible le plus proche.
/// </summary>
public interface IRideDispatcher
{
    Task DispatchAsync(int rideId, CancellationToken cancellationToken);
}
