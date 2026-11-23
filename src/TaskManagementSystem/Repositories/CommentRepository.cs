using Microsoft.EntityFrameworkCore;
using TaskManagementSystem.Data;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Models.DTOs.Comments;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Repositories;

/// <summary>EF Core implementation of <see cref="ICommentRepository"/>.</summary>
public sealed class CommentRepository(ApplicationDbContext dbContext) : ICommentRepository
{
    public async Task<(IReadOnlyList<CommentResponse> Items, int TotalCount)> GetPagedForTaskAsync(
        int taskId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var comments = dbContext.Comments.AsNoTracking().Where(c => c.TaskId == taskId);

        var totalCount = await comments.CountAsync(cancellationToken);

        // Uses IX_Comments_TaskId_CreatedAtUtc (Phase 3): filter + order come straight from the index.
        var items = await ProjectToResponse(comments
                .OrderBy(c => c.CreatedAtUtc)
                .ThenBy(c => c.Id)                 // unique tiebreaker → stable paging
                .Skip((page - 1) * pageSize)
                .Take(pageSize))
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<CommentResponse?> GetResponseByIdAsync(int id, CancellationToken cancellationToken) =>
        ProjectToResponse(dbContext.Comments.AsNoTracking().Where(c => c.Id == id))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Comment?> GetByIdWithAccessDataAsync(int id, CancellationToken cancellationToken) =>
        dbContext.Comments
            .Include(c => c.Task).ThenInclude(t => t.Project).ThenInclude(p => p.Members)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public void Add(Comment comment) => dbContext.Comments.Add(comment);

    public void Remove(Comment comment) => dbContext.Comments.Remove(comment);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private static IQueryable<CommentResponse> ProjectToResponse(IQueryable<Comment> comments) =>
        comments.Select(c => new CommentResponse(
            c.Id,
            c.TaskId,
            c.AuthorId,
            c.Author.FirstName + " " + c.Author.LastName,
            c.Content,
            c.CreatedAtUtc,
            c.UpdatedAtUtc));
}
