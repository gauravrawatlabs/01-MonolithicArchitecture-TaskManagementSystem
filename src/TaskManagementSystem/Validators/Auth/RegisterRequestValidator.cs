using FluentValidation;
using TaskManagementSystem.Models.DTOs.Auth;

namespace TaskManagementSystem.Validators.Auth;

/// <summary>Validates registration input. Lengths match the database columns (Phase 3).</summary>
public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid email address.")
            .MaximumLength(256);

        RuleFor(x => x.Password).StrongPassword();

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(50);

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(50);
    }
}
