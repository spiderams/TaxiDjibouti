using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers.GetMyDriver;
using Taxi.Application.Drivers.SetAvailability;
using Taxi.Domain.Drivers;
using Taxi.SharedKernel;
using Xunit;

namespace Taxi.Application.Tests.Drivers;

public class DriverQueryHandlersTests
{
    private readonly Mock<IRepository<Driver>> _repo = new();

    [Fact]
    public async Task GetMyDriver_should_return_dto_when_found()
    {
        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Driver.Create("u-1", "LIC-001", "DJ-1234", "Taxi"));
        var handler = new GetMyDriverQueryHandler(_repo.Object);

        var result = await handler.Handle(new GetMyDriverQuery("u-1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be("u-1");
    }

    [Fact]
    public async Task GetMyDriver_should_fail_notfound_when_absent()
    {
        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Driver?)null);
        var handler = new GetMyDriverQueryHandler(_repo.Object);

        var result = await handler.Handle(new GetMyDriverQuery("u-x"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task SetAvailability_should_toggle_and_update_when_found()
    {
        var driver = Driver.Create("u-1", "LIC-001", "DJ-1234", "Taxi");
        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(driver);
        var handler = new SetAvailabilityCommandHandler(_repo.Object);

        var result = await handler.Handle(new SetAvailabilityCommand("u-1", true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsAvailable.Should().BeTrue();
        _repo.Verify(r => r.UpdateAsync(driver, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetAvailability_should_fail_notfound_when_absent()
    {
        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Driver?)null);
        var handler = new SetAvailabilityCommandHandler(_repo.Object);

        var result = await handler.Handle(new SetAvailabilityCommand("u-x", true), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }
}
