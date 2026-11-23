namespace TaskManagementSystem.Models.DTOs.Categories;

/// <summary>Data required to update a category (full replacement of editable fields).</summary>
/// <param name="Name">Unique category name (max 100 characters).</param>
/// <param name="Description">Optional description (max 500 characters).</param>
public sealed record UpdateCategoryRequest(string Name, string? Description);
