using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManagementSystem.Enums;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Data.Configurations;

public class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        // Entity class is TaskItem, but the table is called Tasks.
        builder.ToTable("Tasks", table =>
        {
            table.HasCheckConstraint(
                "CK_Tasks_Status",
                CheckConstraintSql.EnumValues<TaskItemStatus>(nameof(TaskItem.Status)));
            table.HasCheckConstraint(
                "CK_Tasks_Priority",
                CheckConstraintSql.EnumValues<TaskPriority>(nameof(TaskItem.Priority)));
        });

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(4000);

        // Project 1 ──< * Tasks. Project deleted → its tasks (and, via Tasks→Comments, their comments) go too.
        builder.HasOne(t => t.Project)
            .WithMany(p => p.Tasks)
            .HasForeignKey(t => t.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Category 1 ──< * Tasks (optional). Category deleted → tasks stay, CategoryId becomes NULL.
        builder.HasOne(t => t.Category)
            .WithMany(c => c.Tasks)
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Two separate relationships to the SAME table (AspNetUsers): creator and assignee.
        // Because there are two, EF Core cannot guess which navigation pairs with which —
        // we must configure both explicitly. Both Restrict (users are never hard-deleted),
        // which also avoids SQL Server's "multiple cascade paths" error.
        builder.HasOne(t => t.CreatedBy)
            .WithMany(u => u.CreatedTasks)
            .HasForeignKey(t => t.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.AssignedTo)
            .WithMany(u => u.AssignedTasks)
            .HasForeignKey(t => t.AssignedToId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes for the most common queries (filtering, "my tasks", dashboard).
        // Composite indexes replace the single-column FK indexes EF would otherwise create.
        builder.HasIndex(t => new { t.ProjectId, t.Status });
        builder.HasIndex(t => new { t.AssignedToId, t.Status });
        builder.HasIndex(t => t.DueDateUtc);
    }
}
