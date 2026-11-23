using Microsoft.EntityFrameworkCore;

namespace TaskManagementSystem.Data;

/// <summary>
/// The application's single EF Core session with SQL Server.
/// In Phase 3 this will inherit from IdentityDbContext and expose the domain DbSets.
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Picks up every IEntityTypeConfiguration<T> class in this assembly
        // (added under Data/Configurations in Phase 3).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
