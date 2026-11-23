using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManagementSystem.Constants;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Models.DTOs.Comments;
using TaskManagementSystem.Models.DTOs.Common;

namespace TaskManagementSystem.Controllers;

/// <summary>
/// Comments. Listing and adding are nested under the task (/api/tasks/{taskId}/comments);
/// editing and deleting address the comment directly (/api/comments/{id}).
/// </summary>
[ApiController]
[Route("api")]
[Tags("Comments")]
[Authorize]
public sealed class CommentsController(ICommentService commentService) : ControllerBase
{
    /// <summary>Lists a task's comments, oldest first.</summary>
    /// <response code="200">One page of comments.</response>
    /// <response code="404">The task doesn't exist or you are not a member of its project.</response>
    [HttpGet("tasks/{taskId:int}/comments")]
    [ProducesResponseType<PagedResult<CommentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<PagedResult<CommentResponse>>> GetComments(
        int taskId, [FromQuery] CommentQueryParameters query, CancellationToken cancellationToken)
    {
        return Ok(await commentService.GetCommentsAsync(taskId, query, User, cancellationToken));
    }

    /// <summary>Adds a comment to a task. Any project member with a verified email.</summary>
    /// <response code="201">Created; Location header points to the task's comment list.</response>
    /// <response code="400">Empty or too long.</response>
    /// <response code="403">Email not verified.</response>
    /// <response code="404">The task doesn't exist or you are not a member of its project.</response>
    [HttpPost("tasks/{taskId:int}/comments")]
    [Authorize(Policy = Policies.EmailVerified)]
    [ProducesResponseType<CommentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<CommentResponse>> Add(
        int taskId, CreateCommentRequest request, CancellationToken cancellationToken)
    {
        var comment = await commentService.AddAsync(taskId, request, User, cancellationToken);
        return CreatedAtAction(nameof(GetComments), new { taskId }, comment);
    }

    /// <summary>Edits a comment. Author only.</summary>
    /// <response code="200">The updated comment (UpdatedAtUtc set = "edited").</response>
    /// <response code="400">Empty or too long.</response>
    /// <response code="403">You are not the author, or email not verified.</response>
    /// <response code="404">The comment doesn't exist or you are not a member of its project.</response>
    [HttpPut("comments/{id:int}")]
    [Authorize(Policy = Policies.EmailVerified)]
    [ProducesResponseType<CommentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<CommentResponse>> Update(
        int id, UpdateCommentRequest request, CancellationToken cancellationToken)
    {
        return Ok(await commentService.UpdateAsync(id, request, User, cancellationToken));
    }

    /// <summary>Deletes a comment. Author, project owner or Admin.</summary>
    /// <response code="204">Deleted.</response>
    /// <response code="403">Not allowed, or email not verified.</response>
    /// <response code="404">The comment doesn't exist or you are not a member of its project.</response>
    [HttpDelete("comments/{id:int}")]
    [Authorize(Policy = Policies.EmailVerified)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await commentService.DeleteAsync(id, User, cancellationToken);
        return NoContent();
    }
}
