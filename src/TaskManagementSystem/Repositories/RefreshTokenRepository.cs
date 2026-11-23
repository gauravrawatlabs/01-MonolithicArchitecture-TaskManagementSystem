using Microsoft.EntityFrameworkCore;
using TaskManagementSystem.Data;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Repositories;

/// <summary>EF Core implementation of <see cref="IRefreshTokenRepository"/>.</summary>
public sealed class RefreshTokenRepository(ApplicationDbContext dbContext) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        dbContext.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken); // uses unique index

    public void Add(RefreshToken refreshToken) => dbContext.RefreshTokens.Add(refreshToken);

    public async Task<bool> TryRevokeAsync(
        Guid tokenId, DateTime revokedAtUtc, string? revokedByIp, string reason, Guid? replacedByTokenId,
        CancellationToken cancellationToken)
    {
        // ExecuteUpdateAsync sends ONE UPDATE statement directly to SQL Server (no loading, no
        // change tracking). The "RevokedAtUtc == null" condition makes it a compare-and-set:
        // if two requests race with the same token, only one UPDATE can affect the row.
        var affectedRows = await dbContext.RefreshTokens
            .Where(t => t.Id == tokenId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.RevokedAtUtc, revokedAtUtc)
                    .SetProperty(t => t.RevokedByIp, revokedByIp)
                    .SetProperty(t => t.RevokedReason, reason)
                    .SetProperty(t => t.ReplacedByTokenId, replacedByTokenId),
                cancellationToken);

        return affectedRows == 1;
    }

    public Task<int> RevokeAllActiveForUserAsync(
        Guid userId, DateTime revokedAtUtc, string? revokedByIp, string reason, CancellationToken cancellationToken) =>
        dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null && t.ExpiresAtUtc > revokedAtUtc)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.RevokedAtUtc, revokedAtUtc)
                    .SetProperty(t => t.RevokedByIp, revokedByIp)
                    .SetProperty(t => t.RevokedReason, reason),
                cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
