using ECommerPipeline.Domain.Common;

namespace ECommerPipeline.Domain.Entities;

/// <summary>
/// Refresh token storage. Access tokens (JWT) are short-lived (~1h); when
/// they expire, the client exchanges the long-lived refresh token (~7d)
/// for a new access token without re-typing credentials.
/// Stored in DB so we can revoke them server-side (logout, security
/// incident, etc.) — JWT alone can't be invalidated before expiry.
/// </summary>
public class RefreshToken : BaseEntity
{
    public long CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public string Token { get; set; } = null!;            // random 256-bit secret
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByToken { get; set; }          // chain for audit

    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}
