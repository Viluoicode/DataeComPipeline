using FluentAssertions;
using Xunit;
using BC = BCrypt.Net.BCrypt;

namespace ECommerPipeline.Infrastructure.Tests.Auth;

/// <summary>
/// Documents the password-hashing contract used by AuthService.Register/Login.
/// These verify the BCrypt behaviour our auth flow relies on.
/// </summary>
public class PasswordHashingTests
{
    [Fact]
    public void Hash_then_verify_succeeds_for_correct_password()
    {
        var hash = BC.HashPassword("secret123", workFactor: 11);

        BC.Verify("secret123", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_fails_for_wrong_password()
    {
        var hash = BC.HashPassword("secret123", workFactor: 11);

        BC.Verify("wrong-password", hash).Should().BeFalse();
    }

    [Fact]
    public void Same_password_produces_different_hashes_due_to_salt()
    {
        var hash1 = BC.HashPassword("secret123", workFactor: 11);
        var hash2 = BC.HashPassword("secret123", workFactor: 11);

        // Salt is embedded + random, so identical passwords hash differently
        hash1.Should().NotBe(hash2);
        // But both still verify
        BC.Verify("secret123", hash1).Should().BeTrue();
        BC.Verify("secret123", hash2).Should().BeTrue();
    }
}
