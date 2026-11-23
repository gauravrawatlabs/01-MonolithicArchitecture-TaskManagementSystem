using Microsoft.EntityFrameworkCore;
using TaskManagementSystem.Data;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Repositories;

/// <summary>EF Core implementation of <see cref="ICategoryRepository"/>.</summary>
public sealed class CategoryRepository(ApplicationDbContext dbContext) : ICategoryRepository
{
    public async Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken cancellationToken) =>
        await dbContext.Categories
            .AsNoTracking()                 // read-only query: skip change tracking (faster, less memory)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

    public Task<Category?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        dbContext.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<bool> NameExistsAsync(string name, int? excludeId, CancellationToken cancellationToken) =>
        // SQL Server's default collation is case-insensitive, so "bug" matches "Bug" —
        // the same rule the unique index enforces.
        dbContext.Categories.AnyAsync(
            c => c.Name == name && (excludeId == null || c.Id != excludeId),
            cancellationToken);

    public void Add(Category category) => dbContext.Categories.Add(category);

    public void Remove(Category category) => dbContext.Categories.Remove(category);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
