using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Interfaces.Repositories;

/// <summary>Data access for refresh tokens (always looked up by HASH, never by raw value).</summary>
public interface IRefreshTokenRepository
{
    /// <summary>The token with this hash, including its user, or null.</summary>
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);

    void Add(RefreshToken refreshToken);

    /// <summary>
    /// Atomically revokes ONE token, but only if it is not already revoked
    /// (UPDATE ... WHERE Id = @id AND RevokedAtUtc IS NULL).
    /// Returns false if another request revoked it first — the basis of race-safe rotation.
    /// </summary>
    Task<bool> TryRevokeAsync(
        Guid tokenId, DateTime revokedAtUtc, string? revokedByIp, string reason, Guid? replacedByTokenId,
        CancellationToken cancellationToken);

    /// <summary>Revokes every still-active token of a user ("log out everywhere"). Returns the count.</summary>
    Task<int> RevokeAllActiveForUserAsync(
        Guid userId, DateTime revokedAtUtc, string? revokedByIp, string reason, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
