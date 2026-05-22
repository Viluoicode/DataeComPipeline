using ECommerPipeline.Domain.Enums;

namespace ECommerPipeline.Application.Auth.DTOs;

public record RegisterRequest(
    string FullName,
    string Email,
    string Password,
    string? Phone,
    string? City);

public record LoginRequest(string Email, string Password);

public record RefreshRequest(string RefreshToken);

public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    AuthUserDto User);

public record AuthUserDto(
    long Id,
    string FullName,
    string Email,
    UserRole Role);
