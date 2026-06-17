using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Taxi.Application.Dispatch;
using Taxi.Infrastructure.Persistence;

namespace Taxi.Infrastructure.Dispatch;

internal sealed class DriverLocator(AppDbContext db) : IDriverLocator
{
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(10);

    public async Task<IReadOnlyList<NearbyDriver>> FindNearestAsync(
        double latitude, double longitude, double radiusMeters, int max, CancellationToken cancellationToken)
    {
        var pickup = new Point(longitude, latitude) { SRID = 4326 };
        var cutoff = DateTime.UtcNow - FreshnessWindow;

        return await db.Drivers
            .Where(d => d.IsAvailable
                && d.LastLocation != null
                && d.LastLocationAt >= cutoff
                && d.LastLocation.IsWithinDistance(pickup, radiusMeters))
            .OrderBy(d => d.LastLocation!.Distance(pickup))
            .Take(max)
            .Select(d => new NearbyDriver(
                d.Id,
                d.UserId,
                d.LastLocation!.Distance(pickup),
                d.LastLocation!.Y,
                d.LastLocation!.X,
                d.VehicleType))
            .ToListAsync(cancellationToken);
    }
}
