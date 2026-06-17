using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Administration;
using Taxi.Application.Administration.Listing;
using Taxi.Domain.Drivers;
using Xunit;

namespace Taxi.Application.Tests.Administration;

public class AdminListingHandlersTests
{
    [Fact]
    public async Task GetUsers_returns_directory_list()
    {
        var users = new Mock<IUserDirectory>();
        users.Setup(u => u.ListAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<UserSummary> { new("u-1", "Client Test", "77000002", new[] { "Client" }) });
        var handler = new GetUsersQueryHandler(users.Object);

        var result = await handler.Handle(new GetUsersQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].FullName.Should().Be("Client Test");
    }

    [Fact]
    public async Task GetDrivers_maps_to_dtos()
    {
        var drivers = new Mock<IRepository<Driver>>();
        drivers.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new List<Driver> { Driver.Create("u-1", "LIC", "PLATE", "Taxi") });
        var handler = new GetDriversQueryHandler(drivers.Object);

        var result = await handler.Handle(new GetDriversQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }
}
