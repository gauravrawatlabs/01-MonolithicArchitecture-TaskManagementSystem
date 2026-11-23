using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Data.Configurations;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects", table =>
        {
            // DB-level safety net; the validator gives the friendly error first.
            table.HasCheckConstraint(
                "CK_Projects_EndDate_OnOrAfter_StartDate",
                "[StartDate] IS NULL OR [EndDate] IS NULL OR [EndDate] >= [StartDate]");
        });

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(150).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(2000);

        // One-to-many: User (owner) 1 ──< * Projects.
        // Restrict: users are deactivated, never deleted, and deleting a user must not wipe projects.
        builder.HasOne(p => p.Owner)
            .WithMany(u => u.OwnedProjects)
            .HasForeignKey(p => p.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        // One owner cannot have two projects with the same name. Also serves as the OwnerId index.
        builder.HasIndex(p => new { p.OwnerId, p.Name }).IsUnique();
    }
}
