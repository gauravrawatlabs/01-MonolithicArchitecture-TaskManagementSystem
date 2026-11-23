using TaskManagementSystem.Models.DTOs.Auth;
using TaskManagementSystem.Models.DTOs.Users;

namespace TaskManagementSystem.Interfaces.Services;

/// <summary>Account registration, login and "who am I". (Phase 6 adds refresh, logout and password flows.)</summary>
public interface IAuthService
{
    /// <exception cref="Exceptions.ConflictException">Email already registered.</exception>
    /// <exception cref="Exceptions.RequestValidationException">Identity rejected the input.</exception>
    Task<UserProfileResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    /// <exception cref="Exceptions.UnauthorizedException">Invalid credentials, inactive or locked account.</exception>
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    /// <exception cref="Exceptions.UnauthorizedException">The user no longer exists or is inactive.</exception>
    Task<UserProfileResponse> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);
}
