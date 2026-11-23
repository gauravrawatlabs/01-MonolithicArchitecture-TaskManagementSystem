using TaskManagementSystem.Models.DTOs.Users;

namespace TaskManagementSystem.Models.DTOs.Auth;

/// <summary>Returned after a successful login or token refresh.</summary>
/// <param name="AccessToken">JWT to send as "Authorization: Bearer {token}" on protected endpoints.</param>
/// <param name="TokenType">Always "Bearer".</param>
/// <param name="AccessTokenExpiresAtUtc">When the access token expires (UTC) — short-lived (15 min).</param>
/// <param name="RefreshToken">Opaque token for POST /api/auth/refresh. Store it securely; it is shown only once.</param>
/// <param name="RefreshTokenExpiresAtUtc">When the refresh token expires (UTC).</param>
/// <param name="User">The logged-in user's profile.</param>
public sealed record AuthResponse(
    string AccessToken,
    string TokenType,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    UserProfileResponse User);
