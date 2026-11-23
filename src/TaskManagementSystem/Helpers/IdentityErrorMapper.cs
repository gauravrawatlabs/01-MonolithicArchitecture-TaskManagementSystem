using Microsoft.AspNetCore.Identity;
using TaskManagementSystem.Exceptions;

namespace TaskManagementSystem.Helpers;

/// <summary>Converts ASP.NET Core Identity errors into a field-keyed 400 validation error.</summary>
public static class IdentityErrorMapper
{
    public const string InvalidTokenMessage = "The link is invalid or has expired. Please request a new one.";

    /// <param name="result">The failed IdentityResult.</param>
    /// <param name="passwordField">JSON field name for password errors ("password", "newPassword", ...).</param>
    public static RequestValidationException ToValidationException(IdentityResult result, string passwordField)
    {
        var errors = result.Errors
            .GroupBy(error => error.Code switch
            {
                "PasswordMismatch" => "currentPassword",
                var code when code.StartsWith("Password", StringComparison.Ordinal) => passwordField,
                "InvalidEmail" or "InvalidUserName" => "email",
                "InvalidToken" => "token",
                _ => "general"
            })
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(error => error.Code switch
                    {
                        "InvalidToken" => InvalidTokenMessage,
                        "PasswordMismatch" => "The current password is incorrect.",
                        _ => error.Description
                    })
                    .Distinct()
                    .ToArray());

        return new RequestValidationException(errors);
    }

    public static RequestValidationException InvalidToken() =>
        new(new Dictionary<string, string[]> { ["token"] = [InvalidTokenMessage] });
}
