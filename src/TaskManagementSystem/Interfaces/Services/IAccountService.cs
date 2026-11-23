using TaskManagementSystem.Models.DTOs.Account;

namespace TaskManagementSystem.Interfaces.Services;

/// <summary>Account maintenance: email verification and password management.</summary>
public interface IAccountService
{
    /// <exception cref="Exceptions.RequestValidationException">Invalid or expired token.</exception>
    Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken);

    /// <summary>Sends a new confirmation email if the account exists and is unconfirmed. Never reveals which.</summary>
    Task ResendConfirmationEmailAsync(ResendConfirmationEmailRequest request, CancellationToken cancellationToken);

    /// <summary>Sends a password-reset email if the account exists. Never reveals whether it does.</summary>
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken);

    /// <exception cref="Exceptions.RequestValidationException">Invalid token or password policy violation.</exception>
    Task ResetPasswordAsync(ResetPasswordRequest request, string? ipAddress, CancellationToken cancellationToken);

    /// <exception cref="Exceptions.RequestValidationException">Wrong current password or policy violation.</exception>
    /// <exception cref="Exceptions.UnauthorizedException">User no longer exists or is inactive.</exception>
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, string? ipAddress, CancellationToken cancellationToken);
}
