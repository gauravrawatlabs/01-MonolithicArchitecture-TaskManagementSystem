using TaskManagementSystem.Models.DTOs.Users;

namespace TaskManagementSystem.Models.DTOs.Auth;

/// <summary>Returned after a successful login. (Phase 6 adds a refresh token.)</summary>
/// <param name="AccessToken">JWT to send as "Authorization: Bearer {token}" on protected endpoints.</param>
/// <param name="TokenType">Always "Bearer".</param>
/// <param name="ExpiresAtUtc">When the access token expires (UTC).</param>
/// <param name="User">The logged-in user's profile.</param>
public sealed record AuthResponse(
    string AccessToken,
    string TokenType,
    DateTime ExpiresAtUtc,
    UserProfileResponse User);
