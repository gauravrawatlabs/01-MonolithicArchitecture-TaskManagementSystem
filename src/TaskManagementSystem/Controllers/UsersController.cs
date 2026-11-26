using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManagementSystem.Constants;
using TaskManagementSystem.Extensions;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Models.DTOs.Common;
using TaskManagementSystem.Models.DTOs.Users;

namespace TaskManagementSystem.Controllers;

/// <summary>
/// User profiles and admin user management.
/// Shows all four authorization styles:
///   [Authorize]                          → authenticated (class level)
///   Policy = AdminOnly                   → role-based
///   Policy = EmailVerified               → claims-based
///   AdminOnly + EmailVerified together   → policies combine with AND
///   GET {id}                             → resource-based (inside UserService)
/// </summary>
[ApiController]
[Route("api/users")]
[Tags("Users")]
[Authorize]
public sealed class UsersController(IUserService userService) : ControllerBase
{
    /// <summary>Lists users with search, filters and paging. Admin only.</summary>
    /// <response code="200">One page of users.</response>
    /// <response code="400">Invalid query parameters.</response>
    /// <response code="401">Not logged in.</response>
    /// <response code="403">Not an Admin, or email not verified.</response>
    [HttpGet]
    [Authorize(Policy = Policies.AdminOnly)]
    [Authorize(Policy = Policies.EmailVerified)]
    [ProducesResponseType<PagedResult<UserProfileResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<ActionResult<PagedResult<UserProfileResponse>>> GetUsers(
        [FromQuery] UserQueryParameters query, CancellationToken cancellationToken)
    {
        return Ok(await userService.GetUsersAsync(query, cancellationToken));
    }

    /// <summary>Finds active users by name or email, e.g. to add a project member. Admin or Manager.</summary>
    /// <remarks>
    /// Returns at most `limit` (default 10, max 20) users, ordered by name. Not paged — it is meant for
    /// an autocomplete box. `search` must be at least 2 characters. Returns only id, name, email and job title.
    /// </remarks>
    /// <response code="200">The matching users (possibly empty).</response>
    /// <response code="400">Missing or too short search, or invalid limit.</response>
    /// <response code="403">Not an Admin/Manager, or email not verified.</response>
    [HttpGet("lookup")]
    [Authorize(Policy = Policies.CanLookupUsers)]
    [ProducesResponseType<IReadOnlyList<UserLookupResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<UserLookupResponse>>> Lookup(
        [FromQuery] UserLookupQueryParameters query, CancellationToken cancellationToken)
    {
        return Ok(await userService.LookupAsync(query, cancellationToken));
    }

    /// <summary>Gets a user's profile. Allowed for the user themselves or an Admin.</summary>
    /// <response code="200">The profile.</response>
    /// <response code="403">This is not your account and you are not an Admin.</response>
    /// <response code="404">No such user.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<UserProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<UserProfileResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await userService.GetByIdAsync(id, User, cancellationToken));
    }

    /// <summary>Updates your own profile. Requires a verified email address.</summary>
    /// <response code="200">The updated profile.</response>
    /// <response code="400">Invalid input.</response>
    /// <response code="403">Email address not verified yet.</response>
    [HttpPut("me")]
    [Authorize(Policy = Policies.EmailVerified)]
    [ProducesResponseType<UserProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<ActionResult<UserProfileResponse>> UpdateMyProfile(
        UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        return Ok(await userService.UpdateProfileAsync(User.GetUserId(), request, cancellationToken));
    }

    /// <summary>Replaces a user's roles (e.g. promote to Manager). Admin only.</summary>
    /// <remarks>The user's new roles appear in their next access token (after refresh or login).</remarks>
    /// <response code="200">The updated user.</response>
    /// <response code="400">Unknown or duplicate roles.</response>
    /// <response code="403">Not an Admin, or email not verified.</response>
    /// <response code="404">No such user.</response>
    /// <response code="422">You tried to remove your own Admin role.</response>
    [HttpPut("{id:guid}/roles")]
    [Authorize(Policy = Policies.AdminOnly)]
    [Authorize(Policy = Policies.EmailVerified)]
    [ProducesResponseType<UserProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<UserProfileResponse>> UpdateRoles(
        Guid id, UpdateUserRolesRequest request, CancellationToken cancellationToken)
    {
        return Ok(await userService.UpdateRolesAsync(id, request, User.GetUserId(), cancellationToken));
    }

    /// <summary>Activates or deactivates an account. Deactivation ends all its sessions. Admin only.</summary>
    /// <response code="200">The updated user.</response>
    /// <response code="403">Not an Admin, or email not verified.</response>
    /// <response code="404">No such user.</response>
    /// <response code="422">You tried to deactivate your own account.</response>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = Policies.AdminOnly)]
    [Authorize(Policy = Policies.EmailVerified)]
    [ProducesResponseType<UserProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<UserProfileResponse>> UpdateStatus(
        Guid id, UpdateUserStatusRequest request, CancellationToken cancellationToken)
    {
        return Ok(await userService.UpdateStatusAsync(
            id, request, User.GetUserId(), HttpContext.GetClientIpAddress(), cancellationToken));
    }
}
