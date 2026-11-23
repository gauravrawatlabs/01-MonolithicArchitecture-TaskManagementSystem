using Microsoft.EntityFrameworkCore;
using TaskManagementSystem.Exceptions;
using TaskManagementSystem.Helpers;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Mappings;
using TaskManagementSystem.Models.DTOs.Categories;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Services;

/// <summary>
/// Category business logic: uniqueness rule, timestamps, not-found handling, Entity ↔ DTO mapping.
/// Authorization ("only Admins may change categories") is added in Phases 7–8.
/// </summary>
public sealed class CategoryService(
    ICategoryRepository categoryRepository,
    TimeProvider timeProvider,
    ILogger<CategoryService> logger) : ICategoryService
{
    private const string ResourceName = "Category";

    public async Task<IReadOnlyList<CategoryResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        var categories = await categoryRepository.GetAllAsync(cancellationToken);
        return categories.Select(c => c.ToResponse()).ToList();
    }

    public async Task<CategoryResponse> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var category = await GetExistingAsync(id, cancellationToken);
        return category.ToResponse();
    }

    public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        await EnsureNameIsUniqueAsync(name, excludeId: null, cancellationToken);

        var category = new Category
        {
            Name = name,
            Description = NormalizeOptional(request.Description),
            CreatedAtUtc = UtcNow()
        };

        categoryRepository.Add(category);
        await SaveChangesAsync(name, cancellationToken);

        logger.LogInformation("Category {CategoryId} '{CategoryName}' created", category.Id, category.Name);
        return category.ToResponse();
    }

    public async Task<CategoryResponse> UpdateAsync(int id, UpdateCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await GetExistingAsync(id, cancellationToken);

        var name = request.Name.Trim();
        await EnsureNameIsUniqueAsync(name, excludeId: id, cancellationToken);

        // The entity is tracked, so changing properties is enough — SaveChanges issues the UPDATE.
        category.Name = name;
        category.Description = NormalizeOptional(request.Description);
        category.UpdatedAtUtc = UtcNow();

        await SaveChangesAsync(name, cancellationToken);

        logger.LogInformation("Category {CategoryId} updated", category.Id);
        return category.ToResponse();
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var category = await GetExistingAsync(id, cancellationToken);

        // Tasks using this category are kept: the FK is ON DELETE SET NULL (Phase 3).
        categoryRepository.Remove(category);
        await categoryRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Category {CategoryId} deleted", id);
    }

    private async Task<Category> GetExistingAsync(int id, CancellationToken cancellationToken) =>
        await categoryRepository.GetByIdAsync(id, cancellationToken)
        ?? throw NotFoundException.For(ResourceName, id);

    private async Task EnsureNameIsUniqueAsync(string name, int? excludeId, CancellationToken cancellationToken)
    {
        if (await categoryRepository.NameExistsAsync(name, excludeId, cancellationToken))
        {
            throw DuplicateName(name);
        }
    }

    private async Task SaveChangesAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            await categoryRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DbExceptionHelper.IsUniqueConstraintViolation(exception))
        {
            // Race condition: another request saved the same name between our check and our save.
            // The unique index is the real guarantee; the pre-check just gives a fast, friendly error.
            throw DuplicateName(name);
        }
    }

    private static ConflictException DuplicateName(string name) =>
        new($"A category named '{name}' already exists.");

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // TimeProvider instead of DateTime.UtcNow: unit tests can substitute a fake clock.
    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
