using TaskManagementSystem.Models.DTOs.Auth;
using TaskManagementSystem.Models.DTOs.Users;

namespace TaskManagementSystem.Interfaces.Services;

/// <summary>Sessions: registration, login, token refresh, logout and "who am I".</summary>
public interface IAuthService
{
    /// <exception cref="Exceptions.ConflictException">Email already registered.</exception>
    /// <exception cref="Exceptions.RequestValidationException">Identity rejected the input.</exception>
    Task<UserProfileResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    /// <exception cref="Exceptions.UnauthorizedException">Invalid credentials, inactive or locked account.</exception>
    Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Exchanges a valid refresh token for a NEW access + refresh token pair (rotation).</summary>
    /// <exception cref="Exceptions.UnauthorizedException">Unknown, expired, revoked or reused token.</exception>
    Task<AuthResponse> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Revokes one refresh token (this device). Idempotent; never reveals whether the token existed.</summary>
    Task LogoutAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Revokes all refresh tokens of the user (every device).</summary>
    Task LogoutAllAsync(Guid userId, string? ipAddress, CancellationToken cancellationToken);

    /// <exception cref="Exceptions.UnauthorizedException">The user no longer exists or is inactive.</exception>
    Task<UserProfileResponse> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);
}
