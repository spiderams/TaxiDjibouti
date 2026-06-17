using Ardalis.Specification;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Dispatch;
using Taxi.Application.Dispatch.AcceptOffer;
using Taxi.Application.Dispatch.DeclineOffer;
using Taxi.Application.Realtime;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Dispatch;

public class OfferHandlersTests
{
    private readonly Mock<IRepository<Ride>> _rides = new();
    private readonly Mock<IRepository<Driver>> _drivers = new();
    private readonly Mock<IRealtimeNotifier> _notifier = new();
    private readonly Mock<IRideDispatcher> _dispatcher = new();

    private static Driver DriverWithId(int id)
    {
        var d = Driver.Create("driver-user", "LIC", "PLATE", "Taxi");
        typeof(Taxi.SharedKernel.Entity).GetProperty("Id")!.SetValue(d, id);
        d.SetAvailability(true);
        return d;
    }

    private static Ride OfferedRideTo(int driverId)
    {
        var r = Ride.Request("client-1", "A", "B", "Z1", "Z2", 11.58, 43.14, 11.6, 43.16, 1000m);
        r.Offer(driverId, DateTime.UtcNow.AddSeconds(30));
        return r;
    }

    [Fact]
    public async Task AcceptOffer_assigns_and_makes_driver_unavailable()
    {
        var driver = DriverWithId(5);
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>())).ReturnsAsync(driver);
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>())).ReturnsAsync(OfferedRideTo(5));

        var handler = new AcceptOfferCommandHandler(_rides.Object, _drivers.Object, _notifier.Object, NullLogger<AcceptOfferCommandHandler>.Instance);
        var result = await handler.Handle(new AcceptOfferCommand(1, "driver-user"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Accepted");
        driver.IsAvailable.Should().BeFalse();
        _notifier.Verify(n => n.RideStatusChangedAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), "Accepted", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeclineOffer_marks_tried_returns_pending_and_redispatches()
    {
        var driver = DriverWithId(5);
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>())).ReturnsAsync(driver);
        var ride = OfferedRideTo(5);
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ride);

        var handler = new DeclineOfferCommandHandler(_rides.Object, _drivers.Object, _dispatcher.Object);
        var result = await handler.Handle(new DeclineOfferCommand(1, "driver-user"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ride.TriedDriverIds.Should().Contain(5);
        _dispatcher.Verify(d => d.DispatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
