using FluentAssertions;
using Taxi.Domain.Drivers;
using Xunit;

namespace Taxi.Application.Tests.Drivers;

public class DriverTests
{
    [Fact]
    public void Create_should_set_fields_with_defaults()
    {
        var driver = Driver.Create("u-1", "LIC-001", "DJ-1234", "Taxi");

        driver.UserId.Should().Be("u-1");
        driver.LicenseNumber.Should().Be("LIC-001");
        driver.VehiclePlate.Should().Be("DJ-1234");
        driver.VehicleType.Should().Be("Taxi");
        driver.IsAvailable.Should().BeFalse();
        driver.AverageRating.Should().Be(0);
    }

    [Fact]
    public void UpdateProfile_should_change_profile_fields()
    {
        var driver = Driver.Create("u-1", "LIC-001", "DJ-1234", "Taxi");

        driver.UpdateProfile("LIC-002", "DJ-9999", "Minibus");

        driver.LicenseNumber.Should().Be("LIC-002");
        driver.VehiclePlate.Should().Be("DJ-9999");
        driver.VehicleType.Should().Be("Minibus");
    }

    [Fact]
    public void SetAvailability_should_toggle_availability()
    {
        var driver = Driver.Create("u-1", "LIC-001", "DJ-1234", "Taxi");

        driver.SetAvailability(true);
        driver.IsAvailable.Should().BeTrue();

        driver.SetAvailability(false);
        driver.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void UpdateAverageRating_sets_the_average()
    {
        var driver = Driver.Create("u-1", "LIC", "PLATE", "Taxi");
        driver.UpdateAverageRating(4.5);
        driver.AverageRating.Should().Be(4.5);
    }
}
