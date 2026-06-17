using FluentAssertions;
using Taxi.SharedKernel;
using Xunit;

namespace Taxi.Application.Tests.SharedKernel;

public class ErrorTests
{
    [Fact]
    public void Unauthorized_should_have_unauthorized_type()
    {
        var error = Error.Unauthorized("Auth.Invalid", "Identifiants invalides");
        error.Type.Should().Be(ErrorType.Unauthorized);
        error.Code.Should().Be("Auth.Invalid");
    }

    [Fact]
    public void Forbidden_should_have_forbidden_type()
    {
        var error = Error.Forbidden("X.Forbidden", "Interdit");
        error.Type.Should().Be(ErrorType.Forbidden);
    }
}
