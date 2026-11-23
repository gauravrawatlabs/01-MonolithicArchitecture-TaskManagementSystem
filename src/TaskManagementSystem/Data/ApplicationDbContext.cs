using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TaskManagementSystem.Enums;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Data;

/// <summary>
/// The application's single EF Core session with SQL Server.
/// Inherits IdentityDbContext so the Identity tables (AspNetUsers, AspNetRoles, AspNetUserRoles, ...)
/// live in the same database as our domain tables — one database for the whole monolith.
/// Generic arguments: our user type, the built-in role type, and Guid as the key type for both.
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Comment> Comments => Set<Comment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // MUST run first: configures all Identity tables, keys and relationships.
        base.OnModelCreating(modelBuilder);

        // Picks up every IEntityTypeConfiguration<T> class in this assembly (Data/Configurations).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Store enums as readable strings ("InProgress") instead of ints (1).
        // Safer too: reordering enum members can't silently change the meaning of stored data.
        configurationBuilder.Properties<TaskItemStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<TaskPriority>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ProjectMemberRole>().HaveConversion<string>().HaveMaxLength(20);
    }
}
