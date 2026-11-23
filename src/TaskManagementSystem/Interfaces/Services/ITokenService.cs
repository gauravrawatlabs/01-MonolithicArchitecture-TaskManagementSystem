using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Interfaces.Services;

/// <summary>A signed access token and its expiry time.</summary>
public sealed record AccessToken(string Token, DateTime ExpiresAtUtc);

/// <summary>Creates JWT access tokens. (Phase 6 adds refresh-token generation.)</summary>
public interface ITokenService
{
    AccessToken CreateAccessToken(ApplicationUser user, IEnumerable<string> roles);
}
