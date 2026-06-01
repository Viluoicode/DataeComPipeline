using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ECommerPipeline.Domain.Entities;
using ECommerPipeline.Domain.Enums;
using ECommerPipeline.Infrastructure.Auth;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ECommerPipeline.Infrastructure.Tests.Auth;

public class JwtTokenServiceTests
{
    private static JwtTokenService NewService(int accessMinutes = 60) =>
        new(Options.Create(new JwtOptions
        {
            Secret = "test-secret-at-least-32-characters-long-for-hs256!!",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenMinutes = accessMinutes,
            RefreshTokenDays = 7,
        }));

    private static Customer SampleUser() =>
        new() { Id = 42, FullName = "Admin User", Email = "admin@test.com", Role = UserRole.Admin };

    [Fact]
    public void CreateAccessToken_embeds_expected_claims()
    {
        var sut = NewService();

        var (token, expiresAt) = sut.CreateAccessToken(SampleUser());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == "42");
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "admin@test.com");
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "Admin");
        expiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(60), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void CreateAccessToken_uses_configured_issuer_and_audience()
    {
        var sut = NewService();

        var (token, _) = sut.CreateAccessToken(SampleUser());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Issuer.Should().Be("TestIssuer");
        jwt.Audiences.Should().Contain("TestAudience");
    }

    [Fact]
    public void CreateRefreshToken_is_random_and_unique()
    {
        var sut = NewService();

        var t1 = sut.CreateRefreshToken();
        var t2 = sut.CreateRefreshToken();

        t1.Should().NotBe(t2);
        t1.Should().NotBeNullOrWhiteSpace();
        // 64 random bytes -> base64 length ~88
        t1.Length.Should().BeGreaterThan(80);
    }

    [Fact]
    public void Constructor_rejects_short_secret()
    {
        var act = () => new JwtTokenService(Options.Create(new JwtOptions
        {
            Secret = "too-short",  // < 32 chars
        }));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least 32 chars*");
    }
}
