using FluentValidation;
using TaskManagementSystem.Models.DTOs.Common;

namespace TaskManagementSystem.Validators.Common;

/// <summary>
/// Base validator for list queries: paging rules are defined once and inherited by every
/// query validator (users, projects, tasks, comments).
/// </summary>
public abstract class PagedQueryValidator<T> : AbstractValidator<T> where T : PagedQueryParameters
{
    public const int SearchMaxLength = 100;

    protected PagedQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PagedQueryParameters.MaxPageSize);
    }
}
