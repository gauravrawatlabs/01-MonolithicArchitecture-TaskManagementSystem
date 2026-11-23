using TaskManagementSystem.Models.DTOs.Comments;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Interfaces.Repositories;

/// <summary>Data access for comments.</summary>
public interface ICommentRepository
{
    /// <summary>A page of a task's comments, oldest first, projected to DTOs.</summary>
    Task<(IReadOnlyList<CommentResponse> Items, int TotalCount)> GetPagedForTaskAsync(
        int taskId, int page, int pageSize, CancellationToken cancellationToken);

    Task<CommentResponse?> GetResponseByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>A tracked comment with Task.Project.Members loaded (for the authorization handlers).</summary>
    Task<Comment?> GetByIdWithAccessDataAsync(int id, CancellationToken cancellationToken);

    void Add(Comment comment);

    void Remove(Comment comment);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
