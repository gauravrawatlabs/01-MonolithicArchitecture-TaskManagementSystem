using FluentValidation;
using TaskManagementSystem.Models.DTOs.Categories;

namespace TaskManagementSystem.Validators.Categories;

/// <summary>Limits mirror the Categories table (Phase 3): Name nvarchar(100), Description nvarchar(500).</summary>
public static class CategoryRules
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    public static void ApplyNameRules<T>(IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Name is required.")        // also rejects whitespace-only names
            .MaximumLength(NameMaxLength);

    public static void ApplyDescriptionRules<T>(IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(DescriptionMaxLength);
}

public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        CategoryRules.ApplyNameRules(RuleFor(x => x.Name));
        CategoryRules.ApplyDescriptionRules(RuleFor(x => x.Description));
    }
}

public sealed class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        CategoryRules.ApplyNameRules(RuleFor(x => x.Name));
        CategoryRules.ApplyDescriptionRules(RuleFor(x => x.Description));
    }
}
