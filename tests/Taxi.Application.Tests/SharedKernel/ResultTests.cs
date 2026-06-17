using FluentAssertions;
using Taxi.SharedKernel;
using Xunit;

namespace Taxi.Application.Tests.SharedKernel;

public class ResultTests
{
    [Fact]
    public void Success_should_be_success_with_no_error()
    {
        var result = Result.Success();
        result.IsSuccess.Should().BeTrue();
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_should_carry_the_error()
    {
        var error = Error.NotFound("X.NotFound", "Introuvable");
        var result = Result.Failure(error);
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Generic_success_should_expose_value()
    {
        Result<int> result = 42;
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Accessing_value_on_failure_should_throw()
    {
        var result = Result.Failure<int>(Error.Validation("X", "bad"));
        var act = () => result.Value;
        act.Should().Throw<InvalidOperationException>();
    }
}
