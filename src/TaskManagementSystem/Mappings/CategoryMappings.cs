using TaskManagementSystem.Models.DTOs.Categories;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Mappings;

/// <summary>
/// Manual Entity → DTO mapping. Explicit, compile-time checked and easy to debug —
/// no reflection-based mapper library needed.
/// </summary>
public static class CategoryMappings
{
    public static CategoryResponse ToResponse(this Category category) => new(
        category.Id,
        category.Name,
        category.Description,
        category.CreatedAtUtc,
        category.UpdatedAtUtc);
}
