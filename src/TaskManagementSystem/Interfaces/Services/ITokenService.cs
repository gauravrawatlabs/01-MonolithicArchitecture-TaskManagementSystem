using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Interfaces.Services;

/// <summary>A signed access token and its expiry time.</summary>
public sealed record AccessToken(string Token, DateTime ExpiresAtUtc);

/// <summary>
/// A newly generated refresh token: the raw value goes to the client ONCE;
/// the entity (containing only the hash) goes to the database.
/// </summary>
public sealed record IssuedRefreshToken(string RawToken, RefreshToken Entity);

/// <summary>Creates JWT access tokens and opaque refresh tokens.</summary>
public interface ITokenService
{
    AccessToken CreateAccessToken(ApplicationUser user, IEnumerable<string> roles);

    IssuedRefreshToken CreateRefreshToken(Guid userId, string? createdByIp);

    /// <summary>The hash used to look a refresh token up in the database.</summary>
    string HashRefreshToken(string rawToken);
}
