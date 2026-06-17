using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Administration;
using Taxi.Application.Administration.Stats;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Administration;

public class GetAdminStatsQueryHandlerTests
{
    [Fact]
    public async Task Should_aggregate_counts()
    {
        var users = new Mock<IUserDirectory>();
        users.Setup(u => u.CountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(10);
        var drivers = new Mock<IRepository<Driver>>();
        drivers.Setup(r => r.CountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(3);
        var rides = new Mock<IRepository<Ride>>();
        rides.Setup(r => r.CountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(7);
        var reports = new Mock<IRepository<Report>>();
        reports.Setup(r => r.CountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var handler = new GetAdminStatsQueryHandler(users.Object, drivers.Object, rides.Object, reports.Object);

        var result = await handler.Handle(new GetAdminStatsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new AdminStatsDto(10, 3, 7, 2));
    }
}
