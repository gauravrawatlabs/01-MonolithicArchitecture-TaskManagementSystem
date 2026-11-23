using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManagementSystem.Extensions;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Models.DTOs.Auth;
using TaskManagementSystem.Models.DTOs.Users;

namespace TaskManagementSystem.Controllers;

/// <summary>Registration, login and the current user's profile.</summary>
[ApiController]
[Route("api/auth")]
[Tags("Authentication")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    /// <summary>Creates a new account with the "User" role.</summary>
    /// <response code="201">The account was created.</response>
    /// <response code="400">Invalid input (e.g. weak password, invalid email).</response>
    /// <response code="409">An account with this email already exists.</response>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<UserProfileResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<UserProfileResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = await authService.RegisterAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, user);
    }

    /// <summary>Logs in and returns a JWT access token.</summary>
    /// <remarks>Copy "accessToken" from the response, click "Authorize" in Swagger and paste it.</remarks>
    /// <response code="200">Login succeeded.</response>
    /// <response code="400">Email or password missing / malformed.</response>
    /// <response code="401">Invalid email or password (also for locked or inactive accounts).</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var response = await authService.LoginAsync(request, cancellationToken);
        return Ok(response);
    }

    /// <summary>Returns the profile of the currently logged-in user.</summary>
    /// <response code="200">The current user's profile.</response>
    /// <response code="401">Missing, invalid or expired access token.</response>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<UserProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<UserProfileResponse>> Me(CancellationToken cancellationToken)
    {
        // User = the ClaimsPrincipal the JwtBearer middleware built from the validated token.
        var profile = await authService.GetCurrentUserAsync(User.GetUserId(), cancellationToken);
        return Ok(profile);
    }
}
