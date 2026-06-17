using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Realtime;
using Taxi.Application.Realtime.UpdateDriverLocation;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Realtime;

public class UpdateDriverLocationCommandHandlerTests
{
    private readonly Mock<IRepository<Driver>> _drivers = new();
    private readonly Mock<IRepository<Ride>> _rides = new();

    private UpdateDriverLocationCommandHandler Handler() => new(_drivers.Object, _rides.Object);

    private static Driver DriverProfile() => Driver.Create("driver-user", "LIC", "PLATE", "Taxi");

    private static Ride AssignedActiveRide()
    {
        var ride = Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m);
        ride.Accept(0);
        return ride;
    }

    private static UpdateDriverLocationCommand Command()
        => new("driver-user", 1, 11.58, 43.14, 90.0, 30.0);

    [Fact]
    public async Task Should_persist_location_and_return_broadcast()
    {
        var driver = DriverProfile();
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(driver);
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(AssignedActiveRide());

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ClientId.Should().Be("client-1");
        result.Value.Latitude.Should().Be(11.58);
        result.Value.Longitude.Should().Be(43.14);
        driver.LastLatitude.Should().Be(11.58);
        driver.LastLongitude.Should().Be(43.14);
        driver.LastLocationAt.Should().NotBeNull();
        _drivers.Verify(d => d.UpdateAsync(It.IsAny<Driver>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_fail_when_driver_profile_missing()
    {
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Driver?)null);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RealtimeErrors.DriverNotFound);
    }

    [Fact]
    public async Task Should_fail_when_ride_missing()
    {
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(DriverProfile());
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((Ride?)null);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.NotFound);
    }

    [Fact]
    public async Task Should_fail_when_ride_not_assigned_to_driver()
    {
        var ride = Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m);
        ride.Accept(5);
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(DriverProfile());
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(ride);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RealtimeErrors.RideNotAssigned);
    }

    [Fact]
    public async Task Should_fail_when_ride_completed()
    {
        var ride = Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m);
        ride.Accept(0);
        ride.MarkArrived();
        ride.Start();
        ride.Complete();
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(DriverProfile());
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(ride);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RealtimeErrors.RideNotActive);
    }
}
