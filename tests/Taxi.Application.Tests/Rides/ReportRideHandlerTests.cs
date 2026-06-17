using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Rides.Reporting;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class ReportRideHandlerTests
{
    private readonly Mock<IRepository<Ride>> _rides = new();
    private readonly Mock<IRepository<Taxi.Domain.Rides.Report>> _reports = new();

    private ReportRideCommandHandler Handler() => new(_rides.Object, _reports.Object);

    [Fact]
    public async Task Should_forbid_when_not_owner()
    {
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m));

        var result = await Handler().Handle(new ReportRideCommand(1, "intruder", "Retard", null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.NotAssignedDriver);
    }

    [Fact]
    public async Task Should_create_report_for_own_ride()
    {
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m));

        var result = await Handler().Handle(new ReportRideCommand(1, "client-1", "Retard", "30 min"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Reason.Should().Be("Retard");
        _reports.Verify(r => r.AddAsync(It.IsAny<Taxi.Domain.Rides.Report>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
