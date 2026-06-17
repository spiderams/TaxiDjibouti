using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Dispatch;
using Taxi.Application.Pricing.EstimatePrice;
using Taxi.Application.Rides;
using Taxi.Application.Rides.Request;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class RequestRideHandlerTests
{
    private readonly Mock<IRepository<Ride>> _rides = new();
    private readonly Mock<IQueryHandler<EstimatePriceQuery, EstimatePriceResponse>> _pricing = new();
    private readonly Mock<IRideDispatcher> _dispatcher = new();

    private RequestRideCommandHandler Handler() => new(_rides.Object, _pricing.Object, _dispatcher.Object);

    private void PriceReturns(decimal price) =>
        _pricing.Setup(p => p.Handle(It.IsAny<EstimatePriceQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success(new EstimatePriceResponse("Centre-ville", "Balbala", price)));

    [Fact]
    public async Task Should_create_pending_ride_with_estimated_price()
    {
        PriceReturns(1500m);

        var result = await Handler().Handle(new RequestRideCommand(
            "client-1", "A", "B", "Centre-ville", "Balbala", null, null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Pending");
        result.Value.EstimatedPrice.Should().Be(1500m);
        result.Value.ClientId.Should().Be("client-1");
        _rides.Verify(r => r.AddAsync(It.IsAny<Ride>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_dispatch_the_new_ride()
    {
        PriceReturns(1500m);

        await Handler().Handle(new RequestRideCommand(
            "client-1", "A", "B", "Centre-ville", "Balbala", 11.58, 43.14, 11.6, 43.16), CancellationToken.None);

        _dispatcher.Verify(d => d.DispatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
