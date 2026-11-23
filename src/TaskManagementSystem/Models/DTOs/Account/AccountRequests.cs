namespace TaskManagementSystem.Models.DTOs.Account;

/// <summary>Confirms an email address with the token from the confirmation email.</summary>
/// <param name="UserId">The userId from the email link.</param>
/// <param name="Token">The token from the email link (Base64Url-encoded).</param>
public sealed record ConfirmEmailRequest(Guid UserId, string Token);

/// <summary>Requests a new confirmation email.</summary>
/// <param name="Email">The account's email address.</param>
public sealed record ResendConfirmationEmailRequest(string Email);

/// <summary>Starts the "forgot password" flow.</summary>
/// <param name="Email">The account's email address.</param>
public sealed record ForgotPasswordRequest(string Email);

/// <summary>Sets a new password using the token from the reset email.</summary>
/// <param name="Email">The account's email address.</param>
/// <param name="Token">The token from the reset email (Base64Url-encoded).</param>
/// <param name="NewPassword">The new password (same rules as registration).</param>
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

/// <summary>Changes the password of the logged-in user.</summary>
/// <param name="CurrentPassword">The current password.</param>
/// <param name="NewPassword">The new password (same rules as registration, must differ from the current one).</param>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
