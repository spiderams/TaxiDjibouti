using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Pricing.EstimatePrice;
using Taxi.Domain.Pricing;
using Xunit;

namespace Taxi.Application.Tests.Pricing;

public class EstimatePriceQueryHandlerTests
{
    private readonly Mock<IRepository<ZonePrice>> _repo = new();

    [Fact]
    public async Task Should_return_zone_price_when_found()
    {
        var zp = ZonePrice.Create("Centre-ville", "Balbala", 1500m);
        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ZonePrice>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(zp);
        var handler = new EstimatePriceQueryHandler(_repo.Object);

        var result = await handler.Handle(new EstimatePriceQuery("Centre-ville", "Balbala"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Price.Should().Be(1500m);
    }

    [Fact]
    public async Task Should_return_default_price_when_not_found()
    {
        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ZonePrice>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((ZonePrice?)null);
        var handler = new EstimatePriceQueryHandler(_repo.Object);

        var result = await handler.Handle(new EstimatePriceQuery("X", "Y"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Price.Should().Be(ZonePrice.DefaultPrice);
    }
}
