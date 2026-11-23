using FluentValidation;
using TaskManagementSystem.Models.DTOs.Auth;

namespace TaskManagementSystem.Validators.Auth;

/// <summary>
/// Only checks that credentials are present. The password policy is deliberately NOT applied at
/// login: a policy change must not lock out users whose older passwords don't match new rules,
/// and rule messages would leak hints to attackers.
/// </summary>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid email address.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}
