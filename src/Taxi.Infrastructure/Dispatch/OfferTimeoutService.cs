using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Taxi.Application.Abstractions;
using Taxi.Application.Dispatch;
using Taxi.Application.Rides;
using Taxi.Domain.Rides;

namespace Taxi.Infrastructure.Dispatch;

internal sealed partial class OfferTimeoutService(
    IServiceScopeFactory scopeFactory,
    ILogger<OfferTimeoutService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var rides = scope.ServiceProvider.GetRequiredService<IRepository<Ride>>();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IRideDispatcher>();

                var expired = await rides.ListAsync(new ExpiredOffersSpec(DateTime.UtcNow), stoppingToken);
                foreach (var ride in expired)
                {
                    if (ride.OfferedDriverId is not null)
                        ride.MarkDriverTried(ride.OfferedDriverId.Value);
                    ride.ReturnToPending();
                    await rides.UpdateAsync(ride, stoppingToken);
                    await dispatcher.DispatchAsync(ride.Id, stoppingToken);
                }

                if (expired.Count > 0)
                    LogOffersExpired(logger, expired.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Échec du traitement des offres expirées");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} offre(s) expirée(s) réattribuée(s)")]
    private static partial void LogOffersExpired(ILogger logger, int count);
}
