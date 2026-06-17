using Taxi.Application.Abstractions;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Reporting;

/// <summary>
/// Gère <see cref="ReportRideCommand"/> : vérifie que le client est bien l'auteur de la course
/// puis persiste le signalement.
/// </summary>
internal sealed class ReportRideCommandHandler(
    IRepository<Ride> rides,
    IRepository<Report> reports)
    : ICommandHandler<ReportRideCommand, ReportDto>
{
    public async Task<Result<ReportDto>> Handle(ReportRideCommand command, CancellationToken cancellationToken)
    {
        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<ReportDto>(RideErrors.NotFound);
        if (ride.ClientId != command.ClientId)
            return Result.Failure<ReportDto>(RideErrors.NotAssignedDriver);

        var report = Report.Create(ride.Id, command.ClientId, ride.DriverId, command.Reason, command.Description);
        await reports.AddAsync(report, cancellationToken);

        return ReportDto.From(report);
    }
}
