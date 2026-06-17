using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Rides.MyRides;
using Taxi.Application.Rides.Pending;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class RideQueriesTests
{
    private static Ride Pending() =>
        Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m);

    [Fact]
    public async Task GetMyRides_as_client_returns_client_rides()
    {
        var rides = new Mock<IRepository<Ride>>();
        var drivers = new Mock<IRepository<Driver>>();
        rides.Setup(r => r.ListAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<Ride> { Pending() });
        var handler = new GetMyRidesQueryHandler(rides.Object, drivers.Object);

        var result = await handler.Handle(new GetMyRidesQuery("client-1", AsDriver: false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetMyRides_as_driver_without_profile_returns_empty()
    {
        var rides = new Mock<IRepository<Ride>>();
        var drivers = new Mock<IRepository<Driver>>();
        drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((Driver?)null);
        var handler = new GetMyRidesQueryHandler(rides.Object, drivers.Object);

        var result = await handler.Handle(new GetMyRidesQuery("u-x", AsDriver: true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPendingRides_returns_pending_list()
    {
        var rides = new Mock<IRepository<Ride>>();
        rides.Setup(r => r.ListAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<Ride> { Pending(), Pending() });
        var handler = new GetPendingRidesQueryHandler(rides.Object);

        var result = await handler.Handle(new GetPendingRidesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }
}
