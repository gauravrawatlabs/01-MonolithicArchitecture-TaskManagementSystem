using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManagementSystem.Data.Seed;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Data.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(500);

        // Duplicate names are rejected by the database too (→ 409 Conflict in the API).
        builder.HasIndex(c => c.Name).IsUnique();

        // Default categories so a fresh database is usable immediately.
        builder.HasData(
            CreateCategory(1, "Bug", "Something is broken and needs fixing."),
            CreateCategory(2, "Feature", "New functionality."),
            CreateCategory(3, "Improvement", "Enhancement of existing functionality."),
            CreateCategory(4, "Research", "Investigation or proof of concept."),
            CreateCategory(5, "Documentation", "Writing or updating documentation."));
    }

    private static Category CreateCategory(int id, string name, string description) => new()
    {
        Id = id,
        Name = name,
        Description = description,
        CreatedAtUtc = SeedData.SeedTimestampUtc
    };
}
