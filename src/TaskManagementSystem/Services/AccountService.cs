using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using TaskManagementSystem.Exceptions;
using TaskManagementSystem.Helpers;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Models.DTOs.Account;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Services;

/// <summary>
/// Email verification and password management. Identity generates and validates the one-time tokens
/// (data-protection based, tied to the user's security stamp, 3-hour lifetime).
/// </summary>
public sealed class AccountService(
    UserManager<ApplicationUser> userManager,
    IAccountEmailService accountEmailService,
    IRefreshTokenRepository refreshTokenRepository,
    TimeProvider timeProvider,
    ILogger<AccountService> logger) : IAccountService
{
    public async Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null || !TryDecodeToken(request.Token, out var token))
        {
            throw IdentityErrorMapper.InvalidToken(); // same error as a wrong token: no user enumeration
        }

        if (user.EmailConfirmed)
        {
            return; // idempotent: clicking the link twice is fine
        }

        var result = await userManager.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
        {
            throw IdentityErrorMapper.ToValidationException(result, passwordField: "password");
        }

        // The next access token (login or refresh) contains email_verified = true.
        logger.LogInformation("User {UserId} confirmed their email address", user.Id);
    }

    public async Task ResendConfirmationEmailAsync(ResendConfirmationEmailRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive || user.EmailConfirmed)
        {
            logger.LogInformation("Confirmation email not resent: account unknown, inactive or already confirmed");
            return; // the caller always gets the same 202 response
        }

        await accountEmailService.SendEmailConfirmationAsync(user, cancellationToken);
        logger.LogInformation("Confirmation email resent to user {UserId}", user.Id);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive)
        {
            logger.LogInformation("Password reset not sent: account unknown or inactive");
            return; // same 202 response whether or not the account exists
        }

        await accountEmailService.SendPasswordResetAsync(user, cancellationToken);
        logger.LogInformation("Password reset email sent to user {UserId}", user.Id);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive || !TryDecodeToken(request.Token, out var token))
        {
            throw IdentityErrorMapper.InvalidToken();
        }

        // Validates the token, hashes the new password, and changes the security stamp
        // (which invalidates every other outstanding reset/confirmation token).
        var result = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            throw IdentityErrorMapper.ToValidationException(result, passwordField: "newPassword");
        }

        // The owner proved control of the mailbox: lift any brute-force lockout.
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);

        // Whoever knew the old password may hold sessions → end them all.
        var revoked = await refreshTokenRepository.RevokeAllActiveForUserAsync(
            user.Id, UtcNow(), ipAddress, "Password reset", cancellationToken);

        logger.LogInformation("User {UserId} reset their password; {Count} sessions revoked", user.Id, revoked);
    }

    public async Task ChangePasswordAsync(
        Guid userId, ChangePasswordRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedException("The user account no longer exists or is inactive.");
        }

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            throw IdentityErrorMapper.ToValidationException(result, passwordField: "newPassword");
        }

        var revoked = await refreshTokenRepository.RevokeAllActiveForUserAsync(
            user.Id, UtcNow(), ipAddress, "Password changed", cancellationToken);

        logger.LogInformation("User {UserId} changed their password; {Count} sessions revoked", user.Id, revoked);
    }

    /// <summary>Emails carry Base64Url-encoded tokens; malformed input is treated as an invalid token.</summary>
    private static bool TryDecodeToken(string encodedToken, out string token)
    {
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedToken));
            return true;
        }
        catch (FormatException)
        {
            token = string.Empty;
            return false;
        }
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
