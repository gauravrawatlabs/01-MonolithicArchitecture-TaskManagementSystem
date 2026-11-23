using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManagementSystem.Extensions;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Models.DTOs.Auth;
using TaskManagementSystem.Models.DTOs.Users;

namespace TaskManagementSystem.Controllers;

/// <summary>Registration, login, token refresh, logout and the current user's profile.</summary>
[ApiController]
[Route("api/auth")]
[Tags("Authentication")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    /// <summary>Creates a new account with the "User" role and sends a confirmation email.</summary>
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

    /// <summary>Logs in and returns an access token (15 min) and a refresh token (7 days).</summary>
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
        var response = await authService.LoginAsync(request, HttpContext.GetClientIpAddress(), cancellationToken);
        return Ok(response);
    }

    /// <summary>Exchanges a refresh token for a new access token AND a new refresh token.</summary>
    /// <remarks>
    /// Each refresh token works only once (rotation). Always store the NEW refresh token from the response.
    /// Re-using an old refresh token is treated as theft: all of the user's sessions are revoked.
    /// </remarks>
    /// <response code="200">New token pair.</response>
    /// <response code="401">The refresh token is unknown, expired, revoked or was already used.</response>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var response = await authService.RefreshAsync(request.RefreshToken, HttpContext.GetClientIpAddress(), cancellationToken);
        return Ok(response);
    }

    /// <summary>Logs out this device by revoking its refresh token.</summary>
    /// <remarks>
    /// Anonymous on purpose: possession of the refresh token is the proof, so logout still works
    /// after the access token has expired. The client must also delete both tokens locally.
    /// The current access token remains valid until it expires (max 15 minutes).
    /// </remarks>
    /// <response code="204">Logged out (also returned for unknown or already revoked tokens).</response>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Logout(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(request.RefreshToken, HttpContext.GetClientIpAddress(), cancellationToken);
        return NoContent();
    }

    /// <summary>Logs out from ALL devices by revoking every refresh token of the current user.</summary>
    /// <response code="204">All sessions revoked.</response>
    /// <response code="401">Missing, invalid or expired access token.</response>
    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        await authService.LogoutAllAsync(User.GetUserId(), HttpContext.GetClientIpAddress(), cancellationToken);
        return NoContent();
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
