using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Data.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(t => t.Id);

        // SHA-256 as hex = 64 chars; 128 leaves room if the hashing scheme changes.
        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();

        builder.Property(t => t.CreatedByIp).HasMaxLength(45);  // 45 = max length of an IPv6 string
        builder.Property(t => t.RevokedByIp).HasMaxLength(45);
        builder.Property(t => t.RevokedReason).HasMaxLength(100);

        // One-to-many: User 1 ──< * RefreshTokens. Tokens are useless without their user → Cascade.
        builder.HasOne(t => t.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // One-to-one (self-referencing): a token is replaced by at most ONE newer token.
        // EF Core creates a UNIQUE filtered index on ReplacedByTokenId to enforce the "one" side.
        // SQL Server does not allow cascading actions on a self-referencing FK → NoAction.
        builder.HasOne(t => t.ReplacedByToken)
            .WithOne()
            .HasForeignKey<RefreshToken>(t => t.ReplacedByTokenId)
            .OnDelete(DeleteBehavior.NoAction);

        // UserId is already indexed by the FK convention; ExpiresAtUtc supports cleanup jobs.
        builder.HasIndex(t => t.ExpiresAtUtc);
    }
}
