using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Realtime.RideAccess;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Realtime;

public class RideAccessQueryHandlerTests
{
    private readonly Mock<IRepository<Ride>> _rides = new();
    private readonly Mock<IRepository<Driver>> _drivers = new();

    private RideAccessQueryHandler Handler() => new(_rides.Object, _drivers.Object);

    [Fact]
    public async Task Admin_always_has_access()
    {
        var result = await Handler().Handle(new RideAccessQuery(1, "anyone", true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        _rides.Verify(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Client_of_the_ride_has_access()
    {
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m));

        var result = await Handler().Handle(new RideAccessQuery(1, "client-1", false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task Unrelated_user_has_no_access()
    {
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m));
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Driver?)null);

        var result = await Handler().Handle(new RideAccessQuery(1, "stranger", false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task Missing_ride_means_no_access()
    {
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((Ride?)null);

        var result = await Handler().Handle(new RideAccessQuery(1, "client-1", false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }
}
