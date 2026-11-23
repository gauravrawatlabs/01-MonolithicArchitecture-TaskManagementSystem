using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManagementSystem.Constants;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Models.DTOs.Categories;

namespace TaskManagementSystem.Controllers;

/// <summary>
/// Task categories (Bug, Feature, ...).
/// Read: any logged-in user. Create/update/delete: Admin with a verified email (CanManageCategories).
/// </summary>
[ApiController]
[Route("api/categories")]
[Tags("Categories")]
[Authorize] // explicit, although the fallback policy would require login anyway
public sealed class CategoriesController(ICategoryService categoryService) : ControllerBase
{
    /// <summary>Gets all categories, ordered by name.</summary>
    /// <response code="200">The list of categories.</response>
    /// <response code="401">Not logged in.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<CategoryResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var categories = await categoryService.GetAllAsync(cancellationToken);
        return Ok(categories);
    }

    /// <summary>Gets a single category by its id.</summary>
    /// <param name="id">The category id, e.g. 1.</param>
    /// <param name="cancellationToken">Cancelled automatically if the client disconnects.</param>
    /// <response code="200">The category.</response>
    /// <response code="401">Not logged in.</response>
    /// <response code="404">No category exists with this id.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<CategoryResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        // No null check / try-catch here: the service throws NotFoundException and the
        // ExceptionHandlingMiddleware turns it into a 404 ProblemDetails response.
        var category = await categoryService.GetByIdAsync(id, cancellationToken);
        return Ok(category);
    }

    /// <summary>Creates a category. Admin with a verified email only.</summary>
    /// <response code="201">Created. The Location header contains the new category's URL.</response>
    /// <response code="400">Invalid input.</response>
    /// <response code="403">Not an Admin, or email not verified.</response>
    /// <response code="409">A category with this name already exists (case-insensitive).</response>
    [HttpPost]
    [Authorize(Policy = Policies.CanManageCategories)]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<CategoryResponse>> Create(CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await categoryService.CreateAsync(request, cancellationToken);

        // 201 + Location: /api/categories/{id} — the REST way to say "created, and here it lives".
        return CreatedAtAction(nameof(GetById), new { id = category.Id }, category);
    }

    /// <summary>Updates a category's name and description. Admin with a verified email only.</summary>
    /// <response code="200">The updated category.</response>
    /// <response code="400">Invalid input.</response>
    /// <response code="403">Not an Admin, or email not verified.</response>
    /// <response code="404">No category exists with this id.</response>
    /// <response code="409">Another category already uses this name.</response>
    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.CanManageCategories)]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<CategoryResponse>> Update(
        int id, UpdateCategoryRequest request, CancellationToken cancellationToken)
    {
        return Ok(await categoryService.UpdateAsync(id, request, cancellationToken));
    }

    /// <summary>Deletes a category. Tasks that used it keep existing with no category. Admin with a verified email only.</summary>
    /// <response code="204">Deleted.</response>
    /// <response code="403">Not an Admin, or email not verified.</response>
    /// <response code="404">No category exists with this id.</response>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.CanManageCategories)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await categoryService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
