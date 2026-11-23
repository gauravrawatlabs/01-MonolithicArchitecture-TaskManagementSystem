using FluentValidation;
using TaskManagementSystem.Models.DTOs.Comments;

namespace TaskManagementSystem.Validators.Comments;

/// <summary>Limit mirrors Comments.Content nvarchar(2000) (Phase 3).</summary>
public static class CommentRules
{
    public const int ContentMaxLength = 2000;
}

public sealed class CreateCommentRequestValidator : AbstractValidator<CreateCommentRequest>
{
    public CreateCommentRequestValidator()
    {
        RuleFor(x => x.Content).NotEmpty().WithMessage("Content is required.").MaximumLength(CommentRules.ContentMaxLength);
    }
}

public sealed class UpdateCommentRequestValidator : AbstractValidator<UpdateCommentRequest>
{
    public UpdateCommentRequestValidator()
    {
        RuleFor(x => x.Content).NotEmpty().WithMessage("Content is required.").MaximumLength(CommentRules.ContentMaxLength);
    }
}

public sealed class CommentQueryParametersValidator : AbstractValidator<CommentQueryParameters>
{
    public CommentQueryParametersValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
