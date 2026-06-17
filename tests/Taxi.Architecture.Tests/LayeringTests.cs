using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Taxi.Architecture.Tests;

public class LayeringTests
{
    private const string Domain = "Taxi.Domain";
    private const string Application = "Taxi.Application";
    private const string Infrastructure = "Taxi.Infrastructure";

    [Fact]
    public void Domain_should_not_depend_on_Application_or_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Taxi.Domain.Pricing.ZonePrice).Assembly)
            .ShouldNot().HaveDependencyOnAny(Application, Infrastructure)
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Application_should_not_depend_on_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Taxi.Application.DependencyInjection).Assembly)
            .ShouldNot().HaveDependencyOn(Infrastructure)
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Identity_user_should_live_in_Domain()
    {
        var result = Types.InAssembly(typeof(Taxi.Domain.Identity.ApplicationUser).Assembly)
            .That().HaveNameStartingWith("ApplicationUser")
            .Should().ResideInNamespaceStartingWith("Taxi.Domain")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
