using FluentValidation;
using TaskManagementSystem.Models.DTOs.Projects;
using TaskManagementSystem.Validators.Common;

namespace TaskManagementSystem.Validators.Projects;

/// <summary>Limits mirror the Projects table (Phase 3): Name nvarchar(150), Description nvarchar(2000).</summary>
public static class ProjectRules
{
    public const int NameMaxLength = 150;
    public const int DescriptionMaxLength = 2000;
    public const string EndDateMessage = "End date must be on or after the start date.";

    /// <summary>Same rule as the CK_Projects_EndDate_OnOrAfter_StartDate check constraint — validated here first for a 400.</summary>
    public static bool EndDateOnOrAfterStartDate(DateOnly? startDate, DateOnly? endDate) =>
        startDate is null || endDate is null || endDate >= startDate;
}

public sealed class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.").MaximumLength(ProjectRules.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(ProjectRules.DescriptionMaxLength);
        RuleFor(x => x.EndDate)
            .Must((request, endDate) => ProjectRules.EndDateOnOrAfterStartDate(request.StartDate, endDate))
            .WithMessage(ProjectRules.EndDateMessage);
    }
}

public sealed class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.").MaximumLength(ProjectRules.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(ProjectRules.DescriptionMaxLength);
        RuleFor(x => x.EndDate)
            .Must((request, endDate) => ProjectRules.EndDateOnOrAfterStartDate(request.StartDate, endDate))
            .WithMessage(ProjectRules.EndDateMessage);
    }
}

public sealed class AddProjectMemberRequestValidator : AbstractValidator<AddProjectMemberRequest>
{
    public AddProjectMemberRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("User id is required.");
    }
}

public sealed class ProjectQueryParametersValidator : PagedQueryValidator<ProjectQueryParameters>
{
    public ProjectQueryParametersValidator()
    {
        RuleFor(x => x.Search).MaximumLength(SearchMaxLength);
        RuleFor(x => x.SortBy).IsInEnum();
        RuleFor(x => x.SortDirection).IsInEnum();
    }
}
