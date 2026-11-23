using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Data.Configurations;

/// <summary>
/// Adds our profile columns to Identity's AspNetUsers table.
/// Relationships to other entities are configured from the dependent side
/// (e.g. in TaskItemConfiguration), so each relationship is defined exactly once.
/// </summary>
public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.FirstName).HasMaxLength(50).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(50).IsRequired();
        builder.Property(u => u.JobTitle).HasMaxLength(100);

        // Speeds up the Admin "list users" filter (active / inactive).
        builder.HasIndex(u => u.IsActive);
    }
}
