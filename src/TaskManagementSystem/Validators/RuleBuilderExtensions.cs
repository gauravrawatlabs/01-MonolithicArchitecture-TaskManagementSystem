using FluentValidation;
using TaskManagementSystem.Constants;

namespace TaskManagementSystem.Validators;

/// <summary>Reusable FluentValidation rules shared by several validators.</summary>
public static class RuleBuilderExtensions
{
    /// <summary>
    /// The password policy (mirrors the Identity options in AddIdentityAndJwtAuthentication).
    /// Validating here gives the client ALL problems at once with clear messages; Identity still
    /// enforces the same rules server-side when the password is hashed.
    /// </summary>
    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        ruleBuilder
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(PasswordRules.MinLength)
                .WithMessage($"Password must be at least {PasswordRules.MinLength} characters long.")
            .MaximumLength(PasswordRules.MaxLength)
                .WithMessage($"Password must not exceed {PasswordRules.MaxLength} characters.")
            .Matches("[A-Z]").WithMessage("Password must contain at least one upper-case letter.")
            .Matches("[a-z]").WithMessage("Password must contain at least one lower-case letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character.");
}
