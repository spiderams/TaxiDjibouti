using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Taxi.Domain.Identity;
using Taxi.Infrastructure.Identity;
using Xunit;

namespace Taxi.Application.Tests.Identity;

public class TokenServiceTests
{
    private static TokenService CreateService() =>
        new(Options.Create(new JwtSettings
        {
            Secret = "taxi-djibouti-dev-secret-key-minimum-32-characters!",
            Issuer = "TaxiDjibouti",
            Audience = "TaxiDjiboutiApp",
            AccessTokenLifetimeMinutes = 60
        }));

    [Fact]
    public void CreateAccessToken_should_embed_sub_phone_and_role_claims()
    {
        var service = CreateService();
        var user = new ApplicationUser { Id = "u-1", UserName = "77000002", PhoneNumber = "77000002", FullName = "Client Test" };

        var token = service.CreateAccessToken(user, new[] { RoleNames.Client });

        token.Token.Should().NotBeNullOrWhiteSpace();
        token.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(60), TimeSpan.FromSeconds(10));

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.Token);
        jwt.GetClaim(JwtRegisteredClaimNames.Sub).Value.Should().Be("u-1");
        jwt.GetClaim("phone").Value.Should().Be("77000002");
        jwt.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).Should().Contain(RoleNames.Client);
    }

    [Fact]
    public void HashRefreshToken_should_be_deterministic_lowercase_hex()
    {
        var service = CreateService();
        var hash1 = service.HashRefreshToken("abc");
        var hash2 = service.HashRefreshToken("abc");

        hash1.Should().Be(hash2);
        hash1.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void CreateRefreshToken_should_return_raw_with_matching_hash_and_future_expiry()
    {
        var service = CreateService();
        var rt = service.CreateRefreshToken();

        rt.RawToken.Should().NotBeNullOrWhiteSpace();
        rt.TokenHash.Should().Be(service.HashRefreshToken(rt.RawToken));
        rt.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromSeconds(10));
    }
}
