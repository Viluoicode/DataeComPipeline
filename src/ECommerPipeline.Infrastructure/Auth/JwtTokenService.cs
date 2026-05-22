using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ECommerPipeline.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ECommerPipeline.Infrastructure.Auth;

/// <summary>
/// Issues JWT access tokens (short-lived, ~1h) + opaque refresh tokens
/// (long-lived, ~7d, stored in DB so we can revoke them).
/// </summary>
public class JwtTokenService
{
    private readonly JwtOptions _opt;
    private readonly SigningCredentials _credentials;

    public JwtTokenService(IOptions<JwtOptions> opt)
    {
        _opt = opt.Value;
        var keyBytes = Encoding.UTF8.GetBytes(_opt.Secret);
        if (keyBytes.Length < 32)
            throw new InvalidOperationException("Jwt:Secret must be at least 32 chars (256 bits).");
        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes),
            SecurityAlgorithms.HmacSha256);
    }

    public (string token, DateTime expiresAt) CreateAccessToken(Customer user)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_opt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.Role.ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: _credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    /// <summary>Cryptographically random opaque refresh token.</summary>
    public string CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    public int RefreshTokenDays => _opt.RefreshTokenDays;
}
