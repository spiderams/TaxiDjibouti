using FluentAssertions;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Rides;

/// <summary>
/// Tests de l'ancienne API d'offre individuelle, migrés vers la nouvelle API de vague.
/// Les cas métier identiques sont désormais couverts par RideWaveTests ; ce fichier
/// est conservé pour ne pas perdre les cas de garde (non-offert, état invalide).
/// </summary>
public class RideOfferTests
{
    private static Ride PendingRide()
        => Ride.Request("client-1", "A", "B", "Z1", "Z2", 11.58, 43.14, 11.6, 43.16, 1000m);

    [Fact]
    public void OfferWave_moves_pending_to_offered()
    {
        var ride = PendingRide();
        var result = ride.OfferWave([7], DateTime.UtcNow.AddSeconds(30));

        result.IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.Offered);
        ride.OfferedDriverIds.Should().Contain(7);
        ride.OfferExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public void AcceptOffer_succeeds_for_offered_driver()
    {
        var ride = PendingRide();
        ride.OfferWave([7], DateTime.UtcNow.AddSeconds(30));

        var result = ride.AcceptOffer(7);

        result.IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.Accepted);
        ride.DriverId.Should().Be(7);
        ride.OfferedDriverIds.Should().BeEmpty();
    }

    [Fact]
    public void AcceptOffer_fails_for_wrong_driver()
    {
        var ride = PendingRide();
        ride.OfferWave([7], DateTime.UtcNow.AddSeconds(30));

        var result = ride.AcceptOffer(9);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.OfferMismatch);
    }

    [Fact]
    public void AcceptOffer_fails_when_expired()
    {
        var ride = PendingRide();
        ride.OfferWave([7], DateTime.UtcNow.AddSeconds(-1));

        var result = ride.AcceptOffer(7);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.OfferExpired);
    }

    [Fact]
    public void AcceptOffer_fails_when_not_offered()
    {
        var ride = PendingRide();

        var result = ride.AcceptOffer(7);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.NotOffered);
    }

    [Fact]
    public void ReturnToPending_clears_the_offer()
    {
        var ride = PendingRide();
        ride.OfferWave([7], DateTime.UtcNow.AddSeconds(30));

        var result = ride.ReturnToPending();

        result.IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.Pending);
        ride.OfferedDriverIds.Should().BeEmpty();
    }

    [Fact]
    public void MarkDriverTried_is_idempotent()
    {
        var ride = PendingRide();
        ride.MarkDriverTried(7);
        ride.MarkDriverTried(7);

        ride.TriedDriverIds.Should().BeEquivalentTo(new[] { 7 });
    }
}
