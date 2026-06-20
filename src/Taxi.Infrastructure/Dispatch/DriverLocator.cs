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

        // La distance est calculée en base sur la colonne geography (précis, géodésique).
        // On ramène l'objet Point complet ; l'extraction des coordonnées (.Y/.X) se fait
        // ensuite EN MÉMOIRE car PostGIS n'expose ST_X/ST_Y que pour le type geometry,
        // jamais pour geography.
        var rows = await db.Drivers
            .Where(d => d.IsAvailable
                && d.LastLocation != null
                && d.LastLocationAt >= cutoff
                && d.LastLocation.IsWithinDistance(pickup, radiusMeters))
            .OrderBy(d => d.LastLocation!.Distance(pickup))
            .Take(max)
            .Select(d => new
            {
                d.Id,
                d.UserId,
                Distance = d.LastLocation!.Distance(pickup),
                Location = d.LastLocation!,
                d.VehicleType
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new NearbyDriver(
                r.Id,
                r.UserId,
                r.Distance,
                r.Location.Y,
                r.Location.X,
                r.VehicleType))
            .ToList();
    }
}
