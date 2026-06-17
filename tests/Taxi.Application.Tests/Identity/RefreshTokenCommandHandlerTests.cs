using Ardalis.Specification;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Taxi.Application.Identity.Abstractions;
using Taxi.Application.Identity.Auth;
using Taxi.Application.Identity.Auth.Refresh;
using Taxi.Application.Abstractions;
using Taxi.Domain.Identity;
using Taxi.SharedKernel;
using Xunit;

namespace Taxi.Application.Tests.Identity;

public class RefreshTokenCommandHandlerTests
{
    private readonly Mock<IRepository<RefreshToken>> _repo = new();
    private readonly Mock<ITokenService> _tokens = new();

    private RefreshTokenCommandHandler CreateHandler()
    {
        _tokens.Setup(t => t.HashRefreshToken(It.IsAny<string>())).Returns("hashed");
        var userManager = IdentityMocks.UserManager();
        var issuer = new AuthTokenIssuer(_tokens.Object, _repo.Object);
        return new RefreshTokenCommandHandler(_repo.Object, userManager.Object, _tokens.Object, issuer, NullLogger<RefreshTokenCommandHandler>.Instance);
    }

    [Fact]
    public async Task Should_fail_when_token_not_found()
    {
        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<RefreshToken>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((RefreshToken?)null);

        var result = await CreateHandler().Handle(new RefreshTokenCommand("raw"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Auth.InvalidToken");
    }

    [Fact]
    public async Task Should_detect_reuse_and_revoke_family_when_token_already_revoked()
    {
        var revoked = RefreshToken.Create("u-1", "hashed", DateTime.UtcNow.AddDays(7), Guid.NewGuid());
        revoked.Revoke("Rotation");
        var familyMember = RefreshToken.Create("u-1", "other", DateTime.UtcNow.AddDays(7), revoked.FamilyId);

        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<RefreshToken>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(revoked);
        _repo.Setup(r => r.ListAsync(It.IsAny<ISpecification<RefreshToken>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<RefreshToken> { revoked, familyMember });

        var result = await CreateHandler().Handle(new RefreshTokenCommand("raw"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Auth.TokenReuse");
        familyMember.IsRevoked.Should().BeTrue(); // family revoked
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Should_fail_when_token_expired()
    {
        var expired = RefreshToken.Create("u-1", "hashed", DateTime.UtcNow.AddSeconds(-1), Guid.NewGuid());
        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<RefreshToken>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(expired);

        var result = await CreateHandler().Handle(new RefreshTokenCommand("raw"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Auth.ExpiredToken");
    }
}
