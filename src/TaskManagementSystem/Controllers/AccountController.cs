using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManagementSystem.Extensions;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Models.DTOs.Account;

namespace TaskManagementSystem.Controllers;

/// <summary>Email verification and password management.</summary>
[ApiController]
[Route("api/account")]
[Tags("Account")]
public sealed class AccountController(IAccountService accountService) : ControllerBase
{
    /// <summary>Confirms the email address using the userId and token from the confirmation email.</summary>
    /// <response code="204">Email confirmed (or was already confirmed).</response>
    /// <response code="400">Invalid or expired token.</response>
    [HttpPost("confirm-email")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        await accountService.ConfirmEmailAsync(request, cancellationToken);
        return NoContent();
    }

    /// <summary>Sends a new confirmation email.</summary>
    /// <remarks>Always returns 202, whether or not the account exists (prevents account enumeration).</remarks>
    /// <response code="202">If the account exists and is unconfirmed, an email has been sent.</response>
    [HttpPost("resend-confirmation")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> ResendConfirmation(ResendConfirmationEmailRequest request, CancellationToken cancellationToken)
    {
        await accountService.ResendConfirmationEmailAsync(request, cancellationToken);
        return Accepted();
    }

    /// <summary>Sends a password-reset email.</summary>
    /// <remarks>Always returns 202, whether or not the account exists (prevents account enumeration).</remarks>
    /// <response code="202">If the account exists, a reset email has been sent.</response>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await accountService.ForgotPasswordAsync(request, cancellationToken);
        return Accepted();
    }

    /// <summary>Sets a new password using the token from the reset email. Ends all sessions.</summary>
    /// <response code="204">Password reset; all refresh tokens revoked. Log in with the new password.</response>
    /// <response code="400">Invalid/expired token or password policy violation.</response>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await accountService.ResetPasswordAsync(request, HttpContext.GetClientIpAddress(), cancellationToken);
        return NoContent();
    }

    /// <summary>Changes the current user's password. Ends all sessions (log in again afterwards).</summary>
    /// <response code="204">Password changed; all refresh tokens revoked.</response>
    /// <response code="400">Wrong current password or password policy violation.</response>
    /// <response code="401">Missing, invalid or expired access token.</response>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await accountService.ChangePasswordAsync(User.GetUserId(), request, HttpContext.GetClientIpAddress(), cancellationToken);
        return NoContent();
    }
}
