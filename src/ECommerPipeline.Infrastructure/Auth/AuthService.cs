using ECommerPipeline.Application.Auth;
using ECommerPipeline.Application.Auth.DTOs;
using ECommerPipeline.Domain.Entities;
using ECommerPipeline.Domain.Enums;
using ECommerPipeline.Infrastructure.Persistence.Oltp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BC = BCrypt.Net.BCrypt;

namespace ECommerPipeline.Infrastructure.Auth;

public class AuthService : IAuthService
{
    private readonly OltpDbContext _db;
    private readonly JwtTokenService _jwt;
    private readonly ILogger<AuthService> _logger;

    public AuthService(OltpDbContext db, JwtTokenService jwt, ILogger<AuthService> logger)
    {
        _db = db;
        _jwt = jwt;
        _logger = logger;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest req, CancellationToken ct = default)
    {
        var emailNorm = req.Email.Trim().ToLowerInvariant();

        if (await _db.Customers.AnyAsync(c => c.Email == emailNorm, ct))
            throw new InvalidOperationException("Email is already registered.");

        var customer = new Customer
        {
            FullName     = req.FullName.Trim(),
            Email        = emailNorm,
            Phone        = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim(),
            City         = string.IsNullOrWhiteSpace(req.City)  ? null : req.City.Trim(),
            PasswordHash = BC.HashPassword(req.Password, workFactor: 11),
            Role         = UserRole.Customer,
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Registered user {Email} (id={Id})", emailNorm, customer.Id);

        return await IssueTokensAsync(customer, ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest req, CancellationToken ct = default)
    {
        var emailNorm = req.Email.Trim().ToLowerInvariant();

        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Email == emailNorm, ct);

        if (customer is null
            || string.IsNullOrEmpty(customer.PasswordHash)
            || !BC.Verify(req.Password, customer.PasswordHash))
        {
            // Identical message for both branches — don't leak whether the email exists.
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        customer.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Login success for {Email}", emailNorm);

        return await IssueTokensAsync(customer, ct);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var existing = await _db.RefreshTokens
            .Include(rt => rt.Customer)
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken, ct);

        if (existing is null || !existing.IsActive)
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");

        // Rotate: revoke old, issue new
        var newRefreshToken = _jwt.CreateRefreshToken();
        existing.RevokedAt = DateTime.UtcNow;
        existing.ReplacedByToken = newRefreshToken;

        _db.RefreshTokens.Add(new RefreshToken
        {
            CustomerId = existing.CustomerId,
            Token      = newRefreshToken,
            ExpiresAt  = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays),
        });

        var (access, accessExpiresAt) = _jwt.CreateAccessToken(existing.Customer);

        await _db.SaveChangesAsync(ct);

        return new AuthResponse(
            access,
            newRefreshToken,
            accessExpiresAt,
            ToDto(existing.Customer));
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct = default)
    {
        var existing = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken, ct);

        if (existing is not null && existing.IsActive)
        {
            existing.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<AuthUserDto?> GetCurrentUserAsync(long userId, CancellationToken ct = default)
    {
        var c = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == userId, ct);
        return c is null ? null : ToDto(c);
    }

    // ---- helpers ----
    private async Task<AuthResponse> IssueTokensAsync(Customer customer, CancellationToken ct)
    {
        var (access, accessExpiresAt) = _jwt.CreateAccessToken(customer);
        var refresh = _jwt.CreateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            CustomerId = customer.Id,
            Token      = refresh,
            ExpiresAt  = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays),
        });
        await _db.SaveChangesAsync(ct);

        return new AuthResponse(access, refresh, accessExpiresAt, ToDto(customer));
    }

    private static AuthUserDto ToDto(Customer c) =>
        new(c.Id, c.FullName, c.Email, c.Role);
}
