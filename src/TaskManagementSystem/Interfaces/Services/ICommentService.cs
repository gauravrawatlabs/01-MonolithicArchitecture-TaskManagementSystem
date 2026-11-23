using System.Security.Claims;
using TaskManagementSystem.Models.DTOs.Comments;
using TaskManagementSystem.Models.DTOs.Common;

namespace TaskManagementSystem.Interfaces.Services;

/// <summary>Comments on tasks. Visibility follows the task (project members and Admins).</summary>
public interface ICommentService
{
    Task<PagedResult<CommentResponse>> GetCommentsAsync(
        int taskId, CommentQueryParameters query, ClaimsPrincipal currentUser, CancellationToken cancellationToken);

    Task<CommentResponse> AddAsync(
        int taskId, CreateCommentRequest request, ClaimsPrincipal currentUser, CancellationToken cancellationToken);

    /// <summary>Only the author may edit.</summary>
    Task<CommentResponse> UpdateAsync(
        int id, UpdateCommentRequest request, ClaimsPrincipal currentUser, CancellationToken cancellationToken);

    /// <summary>The author, the project owner or an Admin may delete.</summary>
    Task DeleteAsync(int id, ClaimsPrincipal currentUser, CancellationToken cancellationToken);
}
