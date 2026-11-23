using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Models.DTOs.Categories;

namespace TaskManagementSystem.Controllers;

/// <summary>
/// Task categories (Bug, Feature, ...). Reading requires login (any role).
/// Create/update/delete endpoints (Admin only) are added in Phase 8.
/// </summary>
[ApiController]
[Route("api/categories")]
[Tags("Categories")]
[Authorize] // explicit, although the fallback policy would require login anyway
public sealed class CategoriesController(ICategoryService categoryService) : ControllerBase
{
    /// <summary>Gets all categories, ordered by name.</summary>
    /// <response code="200">The list of categories.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var categories = await categoryService.GetAllAsync(cancellationToken);
        return Ok(categories);
    }

    /// <summary>Gets a single category by its id.</summary>
    /// <param name="id">The category id, e.g. 1.</param>
    /// <param name="cancellationToken">Cancelled automatically if the client disconnects.</param>
    /// <response code="200">The category.</response>
    /// <response code="404">No category exists with this id.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<CategoryResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        // No null check / try-catch here: the service throws NotFoundException and the
        // ExceptionHandlingMiddleware turns it into a 404 ProblemDetails response.
        var category = await categoryService.GetByIdAsync(id, cancellationToken);
        return Ok(category);
    }
}
