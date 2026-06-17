using Taxi.Application.Abstractions;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Administration.Stats;

/// <summary>
/// Gère <see cref="GetAdminStatsQuery"/> : agrège en parallèle les compteurs d'utilisateurs, de chauffeurs,
/// de courses et de signalements pour retourner un snapshot <see cref="AdminStatsDto"/>.
/// </summary>
internal sealed class GetAdminStatsQueryHandler(
    IUserDirectory users,
    IRepository<Driver> drivers,
    IRepository<Ride> rides,
    IRepository<Report> reports)
    : IQueryHandler<GetAdminStatsQuery, AdminStatsDto>
{
    public async Task<Result<AdminStatsDto>> Handle(GetAdminStatsQuery query, CancellationToken cancellationToken)
    {
        var userCount = await users.CountAsync(cancellationToken);
        var driverCount = await drivers.CountAsync(cancellationToken);
        var rideCount = await rides.CountAsync(cancellationToken);
        var reportCount = await reports.CountAsync(cancellationToken);

        return new AdminStatsDto(userCount, driverCount, rideCount, reportCount);
    }
}
