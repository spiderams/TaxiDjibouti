using FluentAssertions;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class RideStateMachineTests
{
    private static Ride NewPendingRide() =>
        Ride.Request("client-1", "A", "B", "Centre-ville", "Balbala", null, null, null, null, 1500m);

    [Fact]
    public void Request_should_start_pending()
    {
        var ride = NewPendingRide();
        ride.Status.Should().Be(RideStatus.Pending);
        ride.ClientId.Should().Be("client-1");
        ride.EstimatedPrice.Should().Be(1500m);
    }

    [Fact]
    public void Full_happy_path_should_succeed()
    {
        var ride = NewPendingRide();
        ride.Accept(7).IsSuccess.Should().BeTrue();
        ride.DriverId.Should().Be(7);
        ride.Status.Should().Be(RideStatus.Accepted);
        ride.MarkArrived().IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.DriverArrived);
        ride.Start().IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.InProgress);
        ride.Complete().IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.Completed);
        ride.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Accept_should_fail_when_not_pending()
    {
        var ride = NewPendingRide();
        ride.Accept(7);
        var second = ride.Accept(9);
        second.IsFailure.Should().BeTrue();
        second.Error.Should().Be(RideErrors.NotPending);
    }

    [Fact]
    public void Start_should_fail_when_not_arrived()
    {
        var ride = NewPendingRide();
        ride.Accept(7);
        var result = ride.Start();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.InvalidTransition);
    }

    [Fact]
    public void Complete_should_fail_when_not_in_progress()
    {
        var ride = NewPendingRide();
        var result = ride.Complete();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.InvalidTransition);
    }

    [Fact]
    public void Client_can_cancel_before_in_progress_but_not_after()
    {
        var ride = NewPendingRide();
        ride.Accept(7);
        ride.MarkArrived();
        ride.CancelByClient().IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.Cancelled);

        var inProgress = NewPendingRide();
        inProgress.Accept(7); inProgress.MarkArrived(); inProgress.Start();
        inProgress.CancelByClient().IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Driver_cannot_cancel_a_pending_ride()
    {
        var ride = NewPendingRide();
        var result = ride.CancelByDriver();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.CannotCancel);
    }
}
