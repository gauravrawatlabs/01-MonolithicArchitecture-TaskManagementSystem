namespace TaskManagementSystem.Models.DTOs.Categories;

/// <summary>Data required to create a category.</summary>
/// <param name="Name">Unique category name (max 100 characters).</param>
/// <param name="Description">Optional description (max 500 characters).</param>
public sealed record CreateCategoryRequest(string Name, string? Description);
