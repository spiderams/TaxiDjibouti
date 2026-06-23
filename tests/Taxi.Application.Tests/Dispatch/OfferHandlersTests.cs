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

    private static Driver DriverWithIdAndUserId(int id, string userId)
    {
        var d = Driver.Create(userId, "LIC", "PLATE", "Taxi");
        typeof(Taxi.SharedKernel.Entity).GetProperty("Id")!.SetValue(d, id);
        d.SetAvailability(true);
        return d;
    }

    /// <summary>
    /// Crée une course au statut Offered avec une vague contenant uniquement le chauffeur indiqué.
    /// </summary>
    private static Ride OfferedRideTo(int driverId)
    {
        var r = Ride.Request("client-1", "A", "B", "Z1", "Z2", 11.58, 43.14, 11.6, 43.16, 1000m);
        r.OfferWave([driverId], DateTime.UtcNow.AddSeconds(30));
        return r;
    }

    /// <summary>
    /// Crée une course au statut Offered avec une vague de 3 chauffeurs.
    /// </summary>
    private static Ride OfferedRideToWave(int driver1Id, int driver2Id, int driver3Id)
    {
        var r = Ride.Request("client-1", "A", "B", "Z1", "Z2", 11.58, 43.14, 11.6, 43.16, 1000m);
        r.OfferWave([driver1Id, driver2Id, driver3Id], DateTime.UtcNow.AddSeconds(30));
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

    /// <summary>
    /// Vérifie que lorsqu'un chauffeur accepte une offre faite à une vague de 3 chauffeurs,
    /// <see cref="IRealtimeNotifier.RideOfferRevokedAsync"/> est appelé exactement une fois par perdant
    /// avec la raison "taken", et jamais pour le gagnant.
    /// </summary>
    [Fact]
    public async Task AcceptOffer_revokes_losers_with_reason_taken_and_never_revokes_winner()
    {
        // Arrange
        const int winnerDriverId = 10;
        const int loser1DriverId = 20;
        const int loser2DriverId = 30;
        const string winnerUserId = "user-winner";
        const string loser1UserId = "user-loser-1";
        const string loser2UserId = "user-loser-2";
        const int rideId = 42;

        var winner = DriverWithIdAndUserId(winnerDriverId, winnerUserId);
        var loser1 = DriverWithIdAndUserId(loser1DriverId, loser1UserId);
        var loser2 = DriverWithIdAndUserId(loser2DriverId, loser2UserId);

        var ride = OfferedRideToWave(winnerDriverId, loser1DriverId, loser2DriverId);
        typeof(Ride).BaseType?.GetProperty("Id")!.SetValue(ride, rideId);

        // Mock : FirstOrDefaultAsync pour le driver (winner) puis pour la course
        _drivers
            .Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(winner);
        _rides
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ride);

        // Mock : ListAsync pour récupérer les perdants (résout leurs UserId pour SignalR)
        _drivers
            .Setup(d => d.ListAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([loser1, loser2]);

        var handler = new AcceptOfferCommandHandler(
            _rides.Object, _drivers.Object, _notifier.Object,
            NullLogger<AcceptOfferCommandHandler>.Instance);

        // Act
        var result = await handler.Handle(new AcceptOfferCommand(rideId, winnerUserId), CancellationToken.None);

        // Assert — résultat
        result.IsSuccess.Should().BeTrue();

        // Assert — révocation : chaque perdant reçoit "taken", le gagnant ne reçoit rien
        _notifier.Verify(
            n => n.RideOfferRevokedAsync(loser1UserId, rideId, "taken", It.IsAny<CancellationToken>()),
            Times.Once,
            "loser1 doit recevoir une révocation avec reason=taken");
        _notifier.Verify(
            n => n.RideOfferRevokedAsync(loser2UserId, rideId, "taken", It.IsAny<CancellationToken>()),
            Times.Once,
            "loser2 doit recevoir une révocation avec reason=taken");
        _notifier.Verify(
            n => n.RideOfferRevokedAsync(winnerUserId, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "le gagnant ne doit PAS recevoir de révocation");
    }
}
