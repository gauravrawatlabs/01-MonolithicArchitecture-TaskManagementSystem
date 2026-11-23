using TaskManagementSystem.Models.DTOs.Common;

namespace TaskManagementSystem.Models.DTOs.Comments;

/// <summary>A comment on a task.</summary>
/// <param name="Id">Comment id.</param>
/// <param name="TaskId">The task the comment belongs to.</param>
/// <param name="AuthorId">The author's user id.</param>
/// <param name="AuthorName">The author's full name.</param>
/// <param name="Content">The comment text.</param>
/// <param name="CreatedAtUtc">When it was written (UTC).</param>
/// <param name="UpdatedAtUtc">When it was last edited (UTC), or null if never edited.</param>
public sealed record CommentResponse(
    int Id,
    int TaskId,
    Guid AuthorId,
    string AuthorName,
    string Content,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

/// <summary>A new comment.</summary>
/// <param name="Content">The comment text (max 2000 characters).</param>
public sealed record CreateCommentRequest(string Content);

/// <summary>New text for an existing comment.</summary>
/// <param name="Content">The comment text (max 2000 characters).</param>
public sealed record UpdateCommentRequest(string Content);

/// <summary>Paging for a task's comments (oldest first). Default page size 50.</summary>
public sealed class CommentQueryParameters : PagedQueryParameters
{
    public CommentQueryParameters()
    {
        PageSize = 50; // a conversation reads better in bigger pages; init setters can be set from a constructor
    }
}
