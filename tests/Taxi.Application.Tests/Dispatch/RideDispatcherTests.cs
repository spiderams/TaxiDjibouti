using Ardalis.Specification;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Dispatch;
using Taxi.Application.Realtime;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Dispatch;

public class RideDispatcherTests
{
    private readonly Mock<IDriverLocator> _locator = new();
    private readonly Mock<IRepository<Ride>> _rides = new();
    private readonly Mock<IRealtimeNotifier> _notifier = new();

    private RideDispatcher Dispatcher() => new(
        _locator.Object, _rides.Object, _notifier.Object,
        NullLogger<RideDispatcher>.Instance);

    private static Ride PendingRideWithCoords()
        => Ride.Request("client-1", "A", "B", "Z1", "Z2", 11.58, 43.14, 11.6, 43.16, 1000m);

    [Fact]
    public async Task Offers_to_nearest_untried_driver()
    {
        var ride = PendingRideWithCoords();
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(ride);
        _locator.Setup(l => l.FindNearestAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<NearbyDriver> { new(5, "driver-5", 100, 11.581, 43.141, "Taxi") });

        await Dispatcher().DispatchAsync(1, CancellationToken.None);

        ride.Status.Should().Be(RideStatus.Offered);
        ride.OfferedDriverId.Should().Be(5);
        _notifier.Verify(n => n.RideOfferedAsync("driver-5", It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Skips_already_tried_driver_and_offers_next()
    {
        var ride = PendingRideWithCoords();
        ride.MarkDriverTried(5);
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(ride);
        _locator.Setup(l => l.FindNearestAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<NearbyDriver>
                {
                    new(5, "driver-5", 100, 11.581, 43.141, "Taxi"),
                    new(8, "driver-8", 200, 11.582, 43.142, "VTC"),
                });

        await Dispatcher().DispatchAsync(1, CancellationToken.None);

        ride.OfferedDriverId.Should().Be(8);
    }

    [Fact]
    public async Task Returns_to_pending_and_notifies_when_no_candidate()
    {
        var ride = PendingRideWithCoords();
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(ride);
        _locator.Setup(l => l.FindNearestAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<NearbyDriver>());

        await Dispatcher().DispatchAsync(1, CancellationToken.None);

        ride.Status.Should().Be(RideStatus.Pending);
        _notifier.Verify(n => n.NewPendingRideAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Skips_when_no_pickup_coordinates()
    {
        var ride = Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m);
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(ride);

        await Dispatcher().DispatchAsync(1, CancellationToken.None);

        ride.Status.Should().Be(RideStatus.Pending);
        _locator.Verify(l => l.FindNearestAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifier.Verify(n => n.NewPendingRideAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
