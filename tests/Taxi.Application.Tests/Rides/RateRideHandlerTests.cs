using Ardalis.Specification;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Rides.Rate;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class RateRideHandlerTests
{
    private readonly Mock<IRepository<Ride>> _rides = new();
    private readonly Mock<IRepository<Rating>> _ratings = new();
    private readonly Mock<IRepository<Driver>> _drivers = new();

    private RateRideCommandHandler Handler() => new(_rides.Object, _ratings.Object, _drivers.Object, NullLogger<RateRideCommandHandler>.Instance);

    private static Ride CompletedRideOwnedBy(string clientId, int driverId)
    {
        var r = Ride.Request(clientId, "A", "B", "Z1", "Z2", null, null, null, null, 1000m);
        r.Accept(driverId); r.MarkArrived(); r.Start(); r.Complete();
        return r;
    }

    [Fact]
    public async Task Should_forbid_when_not_owner()
    {
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CompletedRideOwnedBy("client-1", 5));

        var result = await Handler().Handle(new RateRideCommand(1, "intruder", 4, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.NotAssignedDriver);
    }

    [Fact]
    public async Task Should_conflict_when_not_completed()
    {
        var pending = Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m);
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(pending);

        var result = await Handler().Handle(new RateRideCommand(1, "client-1", 4, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RatingErrors.RideNotCompleted);
    }

    [Fact]
    public async Task Should_create_rating_and_update_driver_average()
    {
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CompletedRideOwnedBy("client-1", 5));
        _ratings.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Rating>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Rating?)null);
        _ratings.Setup(r => r.ListAsync(It.IsAny<ISpecification<Rating>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Rating> { Rating.Create(1, "client-1", 5, 4, null) });
        var driver = Driver.Create("driver-user", "LIC", "PLATE", "Taxi");
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(driver);

        var result = await Handler().Handle(new RateRideCommand(1, "client-1", 4, "ok"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Score.Should().Be(4);
        driver.AverageRating.Should().Be(4);
        _ratings.Verify(r => r.AddAsync(It.IsAny<Rating>(), It.IsAny<CancellationToken>()), Times.Once);
        _drivers.Verify(d => d.UpdateAsync(driver, It.IsAny<CancellationToken>()), Times.Once);
    }
}
