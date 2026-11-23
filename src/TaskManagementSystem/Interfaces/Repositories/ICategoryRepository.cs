using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Interfaces.Repositories;

/// <summary>
/// Data access for categories. Contains NO business rules — only "how do I read/write this data".
/// A specific (not generic) repository: it exposes exactly the queries the service needs.
/// </summary>
public interface ICategoryRepository
{
    /// <summary>All categories ordered by name, read-only (not tracked).</summary>
    Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>A tracked category (so changes can be saved), or null.</summary>
    Task<Category?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>True if another category already uses this name (optionally ignoring one id, for updates).</summary>
    Task<bool> NameExistsAsync(string name, int? excludeId, CancellationToken cancellationToken);

    void Add(Category category);

    void Remove(Category category);

    /// <summary>Commits all pending changes in the current DbContext (the Unit of Work).</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
