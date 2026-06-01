using ECommerPipeline.Application.Auth.DTOs;
using ECommerPipeline.Application.Auth.Validators;
using FluentAssertions;
using Xunit;

namespace ECommerPipeline.Application.Tests.Auth;

public class RegisterRequestValidatorTests
{
    private readonly RegisterRequestValidator _sut = new();

    private static RegisterRequest Valid() =>
        new(FullName: "Nguyen Van A", Email: "a@test.com", Password: "secret123", Phone: "0901234567", City: "HCM");

    [Fact]
    public void Valid_request_passes()
    {
        _sut.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_fullname_fails(string fullName)
    {
        var result = _sut.Validate(Valid() with { FullName = fullName });
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("not-an-email")]   // no @ at all
    [InlineData("@no-local-part")] // missing local part
    [InlineData("")]
    public void Invalid_email_fails(string email)
    {
        var result = _sut.Validate(Valid() with { Email = email });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterRequest.Email));
    }

    [Theory]
    [InlineData("12345")]   // 5 chars — below minimum 6
    [InlineData("")]
    public void Password_too_short_fails(string password)
    {
        var result = _sut.Validate(Valid() with { Password = password });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterRequest.Password));
    }

    [Fact]
    public void Password_exactly_6_chars_passes()
    {
        var result = _sut.Validate(Valid() with { Password = "123456" });
        result.IsValid.Should().BeTrue();
    }
}

public class LoginRequestValidatorTests
{
    private readonly LoginRequestValidator _sut = new();

    [Fact]
    public void Valid_login_passes()
    {
        _sut.Validate(new LoginRequest("a@test.com", "secret")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Missing_password_fails()
    {
        _sut.Validate(new LoginRequest("a@test.com", "")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Invalid_email_fails()
    {
        _sut.Validate(new LoginRequest("bad-email", "secret")).IsValid.Should().BeFalse();
    }
}
