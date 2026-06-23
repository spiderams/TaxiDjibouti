using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Taxi.Application.Abstractions;
using Taxi.Application.Realtime;
using Taxi.Application.Rides;
using Taxi.Domain.Rides;

namespace Taxi.Application.Dispatch;

/// <summary>
/// Orchestre l'attribution automatique d'une course selon la stratégie de vague :
/// sélectionne simultanément jusqu'à <c>min(3, candidats disponibles)</c> chauffeurs les plus proches
/// non encore essayés (via <see cref="IDriverLocator"/>), leur envoie une offre temporisée en parallèle
/// et les notifie en temps réel. Le premier chauffeur qui accepte remporte la course (premier-arrivé-gagne).
/// Sans candidat untried ou sans coordonnées, la course retombe dans le flux manuel.
/// </summary>
internal sealed partial class RideDispatcher(
    IDriverLocator locator,
    IRepository<Ride> rides,
    IRealtimeNotifier notifier,
    ILogger<RideDispatcher> logger)
    : IRideDispatcher
{
    private static readonly TimeSpan OfferTtl = TimeSpan.FromSeconds(15);
    private const double RadiusMeters = 5000;
    private const int MaxCandidates = 20;
    private const int WaveSize = 3;

    /// <summary>
    /// Tente d'offrir la course <paramref name="rideId"/> à une vague de
    /// <c>min(<see cref="WaveSize"/>, candidats disponibles)</c> chauffeurs les plus proches non encore essayés.
    /// La notification ne part que si la persistance de la vague a réussi ; en cas de conflit de concurrence
    /// optimiste (xmin), on abandonne silencieusement car un autre acteur a déjà fait avancer la course.
    /// </summary>
    public async Task DispatchAsync(int rideId, CancellationToken cancellationToken)
    {
        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(rideId), cancellationToken);
        if (ride is null || ride.Status != RideStatus.Pending)
            return;

        if (ride.PickupLatitude is null || ride.PickupLongitude is null)
        {
            LogNoCoordinates(logger, ride.Id);
            await notifier.NewPendingRideAsync(ride.Id, cancellationToken);
            return;
        }

        var candidates = await locator.FindNearestAsync(
            ride.PickupLatitude.Value, ride.PickupLongitude.Value, RadiusMeters, MaxCandidates, cancellationToken);

        var wave = candidates
            .Where(c => !ride.TriedDriverIds.Contains(c.DriverId))
            .Take(WaveSize)
            .ToList();

        if (wave.Count == 0)
        {
            LogNoCandidate(logger, ride.Id);
            await notifier.NewPendingRideAsync(ride.Id, cancellationToken);
            return;
        }

        var expiresAt = DateTime.UtcNow + OfferTtl;
        ride.OfferWave(wave.Select(c => c.DriverId), expiresAt);

        try
        {
            await rides.UpdateAsync(ride, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Un autre acteur (OfferTimeoutService ou un decline concurrent) a déjà fait avancer la course.
            // On abandonne silencieusement pour ne pas notifier une vague non persistée.
            LogConcurrencyConflict(logger, ride.Id);
            return;
        }

        foreach (var candidate in wave)
            await notifier.RideOfferedAsync(candidate.UserId, ride.Id, expiresAt, cancellationToken);

        LogWaveOffered(logger, ride.Id, wave.Count, expiresAt);
    }

    // --- Logs métier (pattern [LoggerMessage] : à reproduire dans les autres handlers au besoin) ---

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Course {RideId} offerte à une vague de {Count} chauffeur(s) (expire à {ExpiresAt:o})")]
    private static partial void LogWaveOffered(ILogger logger, int rideId, int count, DateTime expiresAt);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Aucun chauffeur disponible pour la course {RideId} → retour en attente (flux manuel)")]
    private static partial void LogNoCandidate(ILogger logger, int rideId);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Conflit de concurrence lors de la persistance de la vague pour la course {RideId} — un autre acteur a déjà fait avancer la course, abandon silencieux")]
    private static partial void LogConcurrencyConflict(ILogger logger, int rideId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Course {RideId} sans coordonnées de prise en charge → flux manuel")]
    private static partial void LogNoCoordinates(ILogger logger, int rideId);
}
