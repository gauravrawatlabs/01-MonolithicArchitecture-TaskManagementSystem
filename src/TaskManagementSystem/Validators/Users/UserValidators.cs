using FluentValidation;
using TaskManagementSystem.Constants;
using TaskManagementSystem.Models.DTOs.Users;

namespace TaskManagementSystem.Validators.Users;

public sealed class UserQueryParametersValidator : AbstractValidator<UserQueryParameters>
{
    public UserQueryParametersValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Role)
            .Must(BeAKnownRole!).When(x => !string.IsNullOrWhiteSpace(x.Role))
            .WithMessage($"Role must be one of: {string.Join(", ", Roles.All)}.");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }

    private static bool BeAKnownRole(string role) =>
        Roles.All.Contains(role, StringComparer.OrdinalIgnoreCase);
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().WithMessage("First name is required.").MaximumLength(50);
        RuleFor(x => x.LastName).NotEmpty().WithMessage("Last name is required.").MaximumLength(50);
        RuleFor(x => x.JobTitle).MaximumLength(100);
    }
}

public sealed class UpdateUserRolesRequestValidator : AbstractValidator<UpdateUserRolesRequest>
{
    public UpdateUserRolesRequestValidator()
    {
        RuleFor(x => x.Roles)
            .NotEmpty().WithMessage("At least one role is required.")
            .Must(roles => roles.Distinct(StringComparer.OrdinalIgnoreCase).Count() == roles.Count)
                .WithMessage("Roles must not contain duplicates.");

        RuleForEach(x => x.Roles)
            .Must(role => Roles.All.Contains(role, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Each role must be one of: {string.Join(", ", Roles.All)}.");
    }
}
