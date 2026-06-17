using FluentAssertions;
using Taxi.Domain.Identity;
using Xunit;

namespace Taxi.Application.Tests.Identity;

public class RefreshTokenTests
{
    [Fact]
    public void Create_should_be_active_and_not_revoked()
    {
        var familyId = Guid.NewGuid();
        var token = RefreshToken.Create("u-1", "hash-1", DateTime.UtcNow.AddDays(7), familyId);

        token.UserId.Should().Be("u-1");
        token.TokenHash.Should().Be("hash-1");
        token.FamilyId.Should().Be(familyId);
        token.IsRevoked.Should().BeFalse();
        token.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Revoke_should_mark_revoked_with_reason_and_replacement()
    {
        var token = RefreshToken.Create("u-1", "hash-1", DateTime.UtcNow.AddDays(7), Guid.NewGuid());

        token.Revoke("Rotation", replacedByTokenId: 42);

        token.IsRevoked.Should().BeTrue();
        token.RevokedReason.Should().Be("Rotation");
        token.ReplacedByTokenId.Should().Be(42);
        token.RevokedAt.Should().NotBeNull();
        token.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Expired_token_should_not_be_active()
    {
        var token = RefreshToken.Create("u-1", "hash-1", DateTime.UtcNow.AddSeconds(-1), Guid.NewGuid());
        token.IsActive.Should().BeFalse();
    }
}
