using Taxi.Application.Abstractions;
using Taxi.Application.Rides;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Administration.Listing;

/// <summary>
/// Gère <see cref="GetReportsQuery"/> : charge tous les signalements et les projette en <see cref="ReportDto"/>.
/// </summary>
internal sealed class GetReportsQueryHandler(IRepository<Report> reports)
    : IQueryHandler<GetReportsQuery, IReadOnlyList<ReportDto>>
{
    public async Task<Result<IReadOnlyList<ReportDto>>> Handle(GetReportsQuery query, CancellationToken cancellationToken)
    {
        var list = await reports.ListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<ReportDto>>(list.Select(ReportDto.From).ToList());
    }
}
