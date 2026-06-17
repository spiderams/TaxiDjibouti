using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Application.Drivers.UpsertProfile;
using Taxi.Domain.Drivers;
using Xunit;

namespace Taxi.Application.Tests.Drivers;

public class UpsertDriverProfileHandlerTests
{
    private readonly Mock<IRepository<Driver>> _repo = new();

    [Fact]
    public async Task Should_create_when_no_existing_profile()
    {
        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Driver?)null);
        var handler = new UpsertDriverProfileCommandHandler(_repo.Object);

        var result = await handler.Handle(
            new UpsertDriverProfileCommand("u-1", "LIC-001", "DJ-1234", "Taxi"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.LicenseNumber.Should().Be("LIC-001");
        _repo.Verify(r => r.AddAsync(It.IsAny<Driver>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_update_when_profile_exists()
    {
        var existing = Driver.Create("u-1", "LIC-001", "DJ-1234", "Taxi");
        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(existing);
        var handler = new UpsertDriverProfileCommandHandler(_repo.Object);

        var result = await handler.Handle(
            new UpsertDriverProfileCommand("u-1", "LIC-002", "DJ-9999", "Minibus"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.LicenseNumber.Should().Be("LIC-002");
        result.Value.VehicleType.Should().Be("Minibus");
        _repo.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.AddAsync(It.IsAny<Driver>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
