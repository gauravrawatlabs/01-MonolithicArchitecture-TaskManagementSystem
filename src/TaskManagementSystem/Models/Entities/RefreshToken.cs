namespace TaskManagementSystem.Models.Entities;

/// <summary>
/// A refresh token issued to a user. Only the SHA-256 HASH of the token is stored;
/// the raw token is returned to the client once and never persisted.
/// Rotation: when a token is used, it is revoked and ReplacedByTokenId points to its successor
/// (a self-referencing one-to-one relationship).
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedByIp { get; set; }
    public string? RevokedReason { get; set; }
    public Guid? ReplacedByTokenId { get; set; }

    public ApplicationUser User { get; set; } = null!;
    public RefreshToken? ReplacedByToken { get; set; }

    // Read-only computed members are not mapped to columns by EF Core.
    public bool IsRevoked => RevokedAtUtc is not null;

    public bool IsActiveAt(DateTime utcNow) => !IsRevoked && utcNow < ExpiresAtUtc;
}
