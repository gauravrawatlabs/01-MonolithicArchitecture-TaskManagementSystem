using TaskManagementSystem.Models.DTOs.Categories;

namespace TaskManagementSystem.Interfaces.Services;

/// <summary>
/// Business operations for categories. Works with DTOs at its boundary;
/// entities stay inside the service/repository layers.
/// </summary>
public interface ICategoryService
{
    Task<IReadOnlyList<CategoryResponse>> GetAllAsync(CancellationToken cancellationToken);

    /// <exception cref="Exceptions.NotFoundException">No category with this id.</exception>
    Task<CategoryResponse> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <exception cref="Exceptions.ConflictException">The name is already used.</exception>
    Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken);

    /// <exception cref="Exceptions.NotFoundException">No category with this id.</exception>
    /// <exception cref="Exceptions.ConflictException">The name is already used by another category.</exception>
    Task<CategoryResponse> UpdateAsync(int id, UpdateCategoryRequest request, CancellationToken cancellationToken);

    /// <exception cref="Exceptions.NotFoundException">No category with this id.</exception>
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
