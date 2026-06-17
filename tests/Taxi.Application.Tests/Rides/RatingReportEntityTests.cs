using FluentAssertions;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class RatingReportEntityTests
{
    [Fact]
    public void Rating_create_then_update_score()
    {
        var rating = Rating.Create(1, "client-1", 5, 4, "bien");
        rating.RideId.Should().Be(1);
        rating.ClientId.Should().Be("client-1");
        rating.DriverId.Should().Be(5);
        rating.Score.Should().Be(4);
        rating.Comment.Should().Be("bien");

        rating.UpdateScore(2, "moyen");
        rating.Score.Should().Be(2);
        rating.Comment.Should().Be("moyen");
    }

    [Fact]
    public void Report_create_sets_fields()
    {
        var report = Report.Create(1, "client-1", 5, "Retard", "30 min");
        report.RideId.Should().Be(1);
        report.ClientId.Should().Be("client-1");
        report.DriverId.Should().Be(5);
        report.Reason.Should().Be("Retard");
        report.Description.Should().Be("30 min");
    }
}
