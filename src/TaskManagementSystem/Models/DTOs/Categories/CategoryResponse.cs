namespace TaskManagementSystem.Models.DTOs.Categories;

/// <summary>A task category as returned by the API.</summary>
/// <param name="Id">Category id.</param>
/// <param name="Name">Unique category name, e.g. "Bug".</param>
/// <param name="Description">Optional description.</param>
/// <param name="CreatedAtUtc">When the category was created (UTC).</param>
/// <param name="UpdatedAtUtc">When the category was last updated (UTC), or null.</param>
public sealed record CategoryResponse(
    int Id,
    string Name,
    string? Description,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
