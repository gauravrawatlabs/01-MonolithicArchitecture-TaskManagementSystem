using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Data.Configurations;

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("Comments");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Content).HasMaxLength(2000).IsRequired();

        // Task 1 ──< * Comments. Comments are part of the task → Cascade.
        builder.HasOne(c => c.Task)
            .WithMany(t => t.Comments)
            .HasForeignKey(c => c.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        // User 1 ──< * Comments. Users are never hard-deleted → Restrict.
        builder.HasOne(c => c.Author)
            .WithMany(u => u.Comments)
            .HasForeignKey(c => c.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        // "Comments of task X, oldest first" — covers the TaskId FK index too.
        builder.HasIndex(c => new { c.TaskId, c.CreatedAtUtc });
    }
}
