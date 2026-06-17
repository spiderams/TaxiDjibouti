using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Realtime;
using Taxi.Application.Rides.Transitions;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class RideTransitionsHandlerTests
{
    private readonly Mock<IRepository<Ride>> _rides = new();
    private readonly Mock<IRepository<Driver>> _drivers = new();
    private readonly Mock<IRealtimeNotifier> _notifier = new();

    private static Driver DriverWithId(int id)
    {
        var d = Driver.Create("driver-user", "LIC", "PLATE", "Taxi");
        typeof(Taxi.SharedKernel.Entity).GetProperty("Id")!.SetValue(d, id);
        d.SetAvailability(true);
        return d;
    }

    private static Ride AcceptedRide(int driverId)
    {
        var r = Ride.Request("c", "A", "B", "Z1", "Z2", null, null, null, null, 1000m);
        r.Accept(driverId);
        return r;
    }

    [Fact]
    public async Task MarkArrived_should_succeed_for_assigned_driver()
    {
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(DriverWithId(7));
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(AcceptedRide(7));
        var handler = new MarkArrivedCommandHandler(_rides.Object, _drivers.Object, _notifier.Object);

        var result = await handler.Handle(new MarkArrivedCommand(1, "driver-user"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("DriverArrived");
        _notifier.Verify(n => n.RideStatusChangedAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), "DriverArrived", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkArrived_should_forbid_when_not_assigned_driver()
    {
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(DriverWithId(9)); // different driver
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(AcceptedRide(7));
        var handler = new MarkArrivedCommandHandler(_rides.Object, _drivers.Object, _notifier.Object);

        var result = await handler.Handle(new MarkArrivedCommand(1, "driver-user"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.NotAssignedDriver);
    }
}
