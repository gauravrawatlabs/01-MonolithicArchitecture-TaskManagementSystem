using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManagementSystem.Constants;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Models.DTOs.Common;
using TaskManagementSystem.Models.DTOs.Projects;

namespace TaskManagementSystem.Controllers;

/// <summary>
/// Projects and their teams.
/// Creating requires the CanCreateProjects policy (Admin or Manager, verified email).
/// Everything else is decided per project (resource-based) inside ProjectService:
/// members can read; the owner and Admins can update, delete and manage members.
/// </summary>
[ApiController]
[Route("api/projects")]
[Tags("Projects")]
[Authorize]
public sealed class ProjectsController(IProjectService projectService) : ControllerBase
{
    /// <summary>Lists the projects you own or are a member of (Admins see all), with search and paging.</summary>
    /// <response code="200">One page of projects.</response>
    /// <response code="400">Invalid query parameters.</response>
    [HttpGet]
    [ProducesResponseType<PagedResult<ProjectSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResult<ProjectSummaryResponse>>> GetProjects(
        [FromQuery] ProjectQueryParameters query, CancellationToken cancellationToken)
    {
        return Ok(await projectService.GetProjectsAsync(query, User, cancellationToken));
    }

    /// <summary>Gets a project with its members.</summary>
    /// <response code="200">The project.</response>
    /// <response code="404">The project doesn't exist or you are not a member.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType<ProjectDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ProjectDetailsResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        return Ok(await projectService.GetByIdAsync(id, User, cancellationToken));
    }

    /// <summary>Creates a project owned by you. Admin or Manager with a verified email.</summary>
    /// <response code="201">Created; Location header points to the project.</response>
    /// <response code="400">Invalid input (e.g. end date before start date).</response>
    /// <response code="403">Not an Admin/Manager, or email not verified.</response>
    /// <response code="409">You already own a project with this name.</response>
    [HttpPost]
    [Authorize(Policy = Policies.CanCreateProjects)]
    [ProducesResponseType<ProjectDetailsResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ProjectDetailsResponse>> Create(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        var project = await projectService.CreateAsync(request, User, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = project.Id }, project);
    }

    /// <summary>Updates a project. Owner or Admin.</summary>
    /// <response code="200">The updated project.</response>
    /// <response code="400">Invalid input.</response>
    /// <response code="403">You are a member but not the owner.</response>
    /// <response code="404">The project doesn't exist or you are not a member.</response>
    /// <response code="409">The owner already has another project with this name.</response>
    [HttpPut("{id:int}")]
    [ProducesResponseType<ProjectDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ProjectDetailsResponse>> Update(
        int id, UpdateProjectRequest request, CancellationToken cancellationToken)
    {
        return Ok(await projectService.UpdateAsync(id, request, User, cancellationToken));
    }

    /// <summary>Deletes a project with all its tasks, comments and memberships. Owner or Admin.</summary>
    /// <response code="204">Deleted.</response>
    /// <response code="403">You are a member but not the owner.</response>
    /// <response code="404">The project doesn't exist or you are not a member.</response>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await projectService.DeleteAsync(id, User, cancellationToken);
        return NoContent();
    }

    /// <summary>Adds a user to the project team. Owner or Admin.</summary>
    /// <response code="200">The project with its updated member list.</response>
    /// <response code="403">You are a member but not the owner.</response>
    /// <response code="404">The project doesn't exist or you are not a member.</response>
    /// <response code="409">The user is already a member.</response>
    /// <response code="422">The user doesn't exist or is inactive.</response>
    [HttpPost("{id:int}/members")]
    [ProducesResponseType<ProjectDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<ProjectDetailsResponse>> AddMember(
        int id, AddProjectMemberRequest request, CancellationToken cancellationToken)
    {
        return Ok(await projectService.AddMemberAsync(id, request, User, cancellationToken));
    }

    /// <summary>Removes a member from the team; their tasks in this project become unassigned. Owner or Admin.</summary>
    /// <response code="204">Removed.</response>
    /// <response code="403">You are a member but not the owner.</response>
    /// <response code="404">The project doesn't exist / you are not a member / the user is not a member.</response>
    /// <response code="422">The owner cannot be removed.</response>
    [HttpDelete("{id:int}/members/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> RemoveMember(int id, Guid userId, CancellationToken cancellationToken)
    {
        await projectService.RemoveMemberAsync(id, userId, User, cancellationToken);
        return NoContent();
    }
}
