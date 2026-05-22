using ECommerPipeline.Application.Auth.DTOs;

namespace ECommerPipeline.Application.Auth;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct = default);
    Task RevokeAsync(string refreshToken, CancellationToken ct = default);
    Task<AuthUserDto?> GetCurrentUserAsync(long userId, CancellationToken ct = default);
}
