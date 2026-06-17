using FluentAssertions;
using Moq;
using Taxi.Application.Dispatch;
using Taxi.Application.Dispatch.FindNearestDrivers;
using Xunit;

namespace Taxi.Application.Tests.Dispatch;

public class FindNearestDriversQueryHandlerTests
{
    [Fact]
    public async Task Should_return_drivers_from_locator()
    {
        var locator = new Mock<IDriverLocator>();
        locator.Setup(l => l.FindNearestAsync(11.58, 43.14, 5000, 10, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new List<NearbyDriver>
               {
                   new(1, "u-1", 120.5, 11.581, 43.141, "Taxi"),
                   new(2, "u-2", 800.0, 11.59, 43.15, "VTC"),
               });
        var handler = new FindNearestDriversQueryHandler(locator.Object);

        var result = await handler.Handle(new FindNearestDriversQuery(11.58, 43.14, 5000, 10), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].DriverId.Should().Be(1);
        result.Value[0].DistanceMeters.Should().Be(120.5);
    }
}
