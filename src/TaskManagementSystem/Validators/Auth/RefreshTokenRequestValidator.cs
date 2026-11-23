using FluentValidation;
using TaskManagementSystem.Models.DTOs.Auth;

namespace TaskManagementSystem.Validators.Auth;

public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required.")
            .MaximumLength(200); // our tokens are 86 chars; reject absurd input before hashing
    }
}
