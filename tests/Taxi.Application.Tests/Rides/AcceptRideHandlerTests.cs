using Ardalis.Specification;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Realtime;
using Taxi.Application.Rides.Accept;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class AcceptRideHandlerTests
{
    private readonly Mock<IRepository<Ride>> _rides = new();
    private readonly Mock<IRepository<Driver>> _drivers = new();
    private readonly Mock<IRealtimeNotifier> _notifier = new();

    private AcceptRideCommandHandler Handler() => new(_rides.Object, _drivers.Object, _notifier.Object, NullLogger<AcceptRideCommandHandler>.Instance);

    private static Driver AvailableDriver()
    {
        var d = Driver.Create("driver-user", "LIC", "PLATE", "Taxi");
        d.SetAvailability(true);
        return d;
    }

    [Fact]
    public async Task Should_accept_when_driver_available_and_ride_pending()
    {
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AvailableDriver());
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Ride.Request("c", "A", "B", "Z1", "Z2", null, null, null, null, 1000m));

        var result = await Handler().Handle(new AcceptRideCommand(1, "driver-user"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Accepted");
        _rides.Verify(r => r.UpdateAsync(It.IsAny<Ride>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.RideStatusChangedAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), "Accepted", It.IsAny<CancellationToken>()), Times.Once);
        _drivers.Verify(d => d.UpdateAsync(It.IsAny<Driver>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_fail_when_no_driver_profile()
    {
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Driver?)null);

        var result = await Handler().Handle(new AcceptRideCommand(1, "x"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.NoDriverProfile);
    }

    [Fact]
    public async Task Should_fail_when_driver_not_available()
    {
        var unavailable = Driver.Create("driver-user", "LIC", "PLATE", "Taxi");
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(unavailable);

        var result = await Handler().Handle(new AcceptRideCommand(1, "driver-user"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.DriverNotAvailable);
    }
}
