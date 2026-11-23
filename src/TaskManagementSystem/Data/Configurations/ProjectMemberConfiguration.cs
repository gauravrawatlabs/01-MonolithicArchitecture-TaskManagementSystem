using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManagementSystem.Enums;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Data.Configurations;

/// <summary>Join entity for the many-to-many relationship Users ↔ Projects.</summary>
public class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("ProjectMembers", table =>
        {
            table.HasCheckConstraint(
                "CK_ProjectMembers_MemberRole",
                CheckConstraintSql.EnumValues<ProjectMemberRole>(nameof(ProjectMember.MemberRole)));
        });

        // Composite primary key: a user can be a member of a given project only once.
        builder.HasKey(m => new { m.ProjectId, m.UserId });

        // Project deleted → its memberships go too.
        builder.HasOne(m => m.Project)
            .WithMany(p => p.Members)
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Users are never hard-deleted → Restrict.
        builder.HasOne(m => m.User)
            .WithMany(u => u.ProjectMemberships)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // The PK index starts with ProjectId ("members of project X").
        // "Projects of user Y" needs its own index on UserId (created by the FK convention).
    }
}
