namespace TaskManagementSystem.Models.DTOs.Auth;

/// <summary>A refresh token, used to get new tokens (refresh) or to end a session (logout).</summary>
/// <param name="RefreshToken">The refreshToken value from the login/refresh response.</param>
public sealed record RefreshTokenRequest(string RefreshToken);
