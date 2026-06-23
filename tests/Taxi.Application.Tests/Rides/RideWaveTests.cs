using FluentAssertions;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class RideWaveTests
{
    private static Ride NewPendingRide() => Ride.Request(
        clientId: "client-1",
        pickupAddress: "A", destinationAddress: "B",
        pickupZone: "Z1", destinationZone: "Z2",
        pickupLatitude: 11.58, pickupLongitude: 43.14,
        destinationLatitude: 11.60, destinationLongitude: 43.15,
        estimatedPrice: 1000m);

    [Fact]
    public void OfferWave_passe_en_Offered_et_remplit_la_vague_et_les_essayes()
    {
        var ride = NewPendingRide();

        var result = ride.OfferWave([10, 20, 30], DateTime.UtcNow.AddSeconds(15));

        result.IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.Offered);
        ride.OfferedDriverIds.Should().BeEquivalentTo([10, 20, 30]);
        ride.TriedDriverIds.Should().BeEquivalentTo([10, 20, 30]);
    }

    [Fact]
    public void OfferWave_echoue_si_pas_pending()
    {
        var ride = NewPendingRide();
        ride.OfferWave([10], DateTime.UtcNow.AddSeconds(15));

        var result = ride.OfferWave([20], DateTime.UtcNow.AddSeconds(15));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.NotPending);
    }

    [Fact]
    public void AcceptOffer_reussit_pour_un_chauffeur_de_la_vague()
    {
        var ride = NewPendingRide();
        ride.OfferWave([10, 20, 30], DateTime.UtcNow.AddSeconds(15));

        var result = ride.AcceptOffer(20);

        result.IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.Accepted);
        ride.DriverId.Should().Be(20);
        ride.OfferedDriverIds.Should().BeEmpty();
    }

    [Fact]
    public void AcceptOffer_echoue_pour_un_chauffeur_hors_vague()
    {
        var ride = NewPendingRide();
        ride.OfferWave([10, 20, 30], DateTime.UtcNow.AddSeconds(15));

        var result = ride.AcceptOffer(99);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.OfferMismatch);
        ride.Status.Should().Be(RideStatus.Offered);
    }

    [Fact]
    public void AcceptOffer_echoue_si_offre_expiree()
    {
        var ride = NewPendingRide();
        ride.OfferWave([10, 20], DateTime.UtcNow.AddSeconds(-1));

        var result = ride.AcceptOffer(10);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.OfferExpired);
    }

    [Fact]
    public void AcceptOffer_echoue_si_deja_accepte_premier_gagne()
    {
        var ride = NewPendingRide();
        ride.OfferWave([10, 20], DateTime.UtcNow.AddSeconds(15));
        ride.AcceptOffer(10); // premier gagne

        var second = ride.AcceptOffer(20); // doit échouer : statut n'est plus Offered

        second.IsFailure.Should().BeTrue();
        second.Error.Should().Be(RideErrors.NotOffered);
    }

    [Fact]
    public void DeclineOffer_retire_de_la_vague_sans_la_vider()
    {
        var ride = NewPendingRide();
        ride.OfferWave([10, 20, 30], DateTime.UtcNow.AddSeconds(15));

        var result = ride.DeclineOffer(20);

        result.IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.Offered); // encore 10 et 30
        ride.OfferedDriverIds.Should().BeEquivalentTo([10, 30]);
    }

    [Fact]
    public void DeclineOffer_vide_la_vague_remet_en_pending()
    {
        var ride = NewPendingRide();
        ride.OfferWave([10], DateTime.UtcNow.AddSeconds(15));

        var result = ride.DeclineOffer(10);

        result.IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.Pending);
        ride.OfferedDriverIds.Should().BeEmpty();
    }
}
