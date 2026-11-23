using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManagementSystem.Constants;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Models.DTOs.Common;
using TaskManagementSystem.Models.DTOs.Tasks;

namespace TaskManagementSystem.Controllers;

/// <summary>
/// Tasks. Reading requires login; every write also requires a verified email (EmailVerified policy).
/// Who may do what on a specific task is decided by TaskAuthorizationHandler inside TaskService.
/// </summary>
[ApiController]
[Route("api/tasks")]
[Tags("Tasks")]
[Authorize]
public sealed class TasksController(ITaskService taskService) : ControllerBase
{
    /// <summary>Lists tasks of projects you belong to (Admins: all), with filters and paging.</summary>
    /// <remarks>Sorted by due date (tasks without a due date last).</remarks>
    /// <response code="200">One page of tasks.</response>
    /// <response code="400">Invalid query parameters.</response>
    [HttpGet]
    [ProducesResponseType<PagedResult<TaskResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResult<TaskResponse>>> GetTasks(
        [FromQuery] TaskQueryParameters query, CancellationToken cancellationToken)
    {
        return Ok(await taskService.GetTasksAsync(query, User, cancellationToken));
    }

    /// <summary>Gets a task.</summary>
    /// <response code="200">The task.</response>
    /// <response code="404">The task doesn't exist or you are not a member of its project.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<TaskResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        return Ok(await taskService.GetByIdAsync(id, User, cancellationToken));
    }

    /// <summary>Creates a task in a project you are a member of. Starts as Todo, unassigned.</summary>
    /// <response code="201">Created; Location header points to the task.</response>
    /// <response code="400">Invalid input (e.g. due date in the past).</response>
    /// <response code="403">Email not verified.</response>
    /// <response code="404">The project doesn't exist or you are not a member.</response>
    /// <response code="422">Unknown category, or due date after the project end date.</response>
    [HttpPost]
    [Authorize(Policy = Policies.EmailVerified)]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<TaskResponse>> Create(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        var task = await taskService.CreateAsync(request, User, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = task.Id }, task);
    }

    /// <summary>Updates a task's details. Project owner, task creator or Admin.</summary>
    /// <response code="200">The updated task.</response>
    /// <response code="400">Invalid input (e.g. a changed due date in the past).</response>
    /// <response code="403">Not allowed to edit this task, or email not verified.</response>
    /// <response code="404">The task doesn't exist or you are not a member of its project.</response>
    /// <response code="422">Unknown category, or due date after the project end date.</response>
    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.EmailVerified)]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<TaskResponse>> Update(int id, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        return Ok(await taskService.UpdateAsync(id, request, User, cancellationToken));
    }

    /// <summary>Assigns the task to a project member, or unassigns it (null). Project owner or Admin.</summary>
    /// <response code="200">The updated task.</response>
    /// <response code="403">Not the project owner/Admin, or email not verified.</response>
    /// <response code="404">The task doesn't exist or you are not a member of its project.</response>
    /// <response code="422">The assignee is not an active member of the project.</response>
    [HttpPatch("{id:int}/assignee")]
    [Authorize(Policy = Policies.EmailVerified)]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<TaskResponse>> Assign(int id, AssignTaskRequest request, CancellationToken cancellationToken)
    {
        return Ok(await taskService.AssignAsync(id, request, User, cancellationToken));
    }

    /// <summary>Moves the task through the workflow. Assignee, creator, project owner or Admin.</summary>
    /// <remarks>
    /// Todo ⇄ InProgress ⇄ InReview → Done; Todo/InProgress/InReview → Cancelled.
    /// Done → InProgress and Cancelled → Todo: project owner or Admin only.
    /// </remarks>
    /// <response code="200">The updated task (CompletedAtUtc set when moved to Done).</response>
    /// <response code="403">Not allowed to change this task's status, or email not verified.</response>
    /// <response code="404">The task doesn't exist or you are not a member of its project.</response>
    /// <response code="409">The workflow doesn't allow this transition.</response>
    [HttpPatch("{id:int}/status")]
    [Authorize(Policy = Policies.EmailVerified)]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<TaskResponse>> ChangeStatus(
        int id, ChangeTaskStatusRequest request, CancellationToken cancellationToken)
    {
        return Ok(await taskService.ChangeStatusAsync(id, request, User, cancellationToken));
    }

    /// <summary>Deletes a task and its comments. Project owner, task creator or Admin.</summary>
    /// <response code="204">Deleted.</response>
    /// <response code="403">Not allowed to delete this task, or email not verified.</response>
    /// <response code="404">The task doesn't exist or you are not a member of its project.</response>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.EmailVerified)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await taskService.DeleteAsync(id, User, cancellationToken);
        return NoContent();
    }
}
