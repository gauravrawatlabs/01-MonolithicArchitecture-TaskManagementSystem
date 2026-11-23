using FluentValidation;
using TaskManagementSystem.Helpers;
using TaskManagementSystem.Models.DTOs.Tasks;

namespace TaskManagementSystem.Validators.Tasks;

/// <summary>Limits mirror the Tasks table (Phase 3): Title nvarchar(200), Description nvarchar(4000).</summary>
public static class TaskRules
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
    public const string DueDateInPastMessage = "Due date must be in the future.";
}

/// <summary>
/// Validates a new task. The validator depends on TimeProvider (injected by DI) to decide what
/// "in the future" means — tests can pass a fake clock. Rules that need the DATABASE (project
/// membership, project end date, category exists) belong in the service, not here.
/// </summary>
public sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.ProjectId).GreaterThan(0).WithMessage("Project id is required.");
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.").MaximumLength(TaskRules.TitleMaxLength);
        RuleFor(x => x.Description).MaximumLength(TaskRules.DescriptionMaxLength);
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.CategoryId).GreaterThan(0).When(x => x.CategoryId is not null);
        RuleFor(x => x.DueDateUtc)
            .Must(due => due is null || due.Value.AsUtc() > timeProvider.GetUtcNow().UtcDateTime)
            .WithMessage(TaskRules.DueDateInPastMessage);
    }
}

/// <summary>
/// Validates a task update. The "due date in the future" rule is NOT applied here: an overdue task
/// must still be editable (e.g. fix a typo in the title). The service checks the due date only
/// when it actually CHANGES.
/// </summary>
public sealed class UpdateTaskRequestValidator : AbstractValidator<UpdateTaskRequest>
{
    public UpdateTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.").MaximumLength(TaskRules.TitleMaxLength);
        RuleFor(x => x.Description).MaximumLength(TaskRules.DescriptionMaxLength);
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.CategoryId).GreaterThan(0).When(x => x.CategoryId is not null);
    }
}

public sealed class AssignTaskRequestValidator : AbstractValidator<AssignTaskRequest>
{
    public AssignTaskRequestValidator()
    {
        RuleFor(x => x.AssignedToId)
            .NotEqual(Guid.Empty).WithMessage("Assignee id must be a valid user id, or null to unassign.");
    }
}

public sealed class ChangeTaskStatusRequestValidator : AbstractValidator<ChangeTaskStatusRequest>
{
    public ChangeTaskStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
    }
}

public sealed class TaskQueryParametersValidator : AbstractValidator<TaskQueryParameters>
{
    public TaskQueryParametersValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0).When(x => x.ProjectId is not null);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
