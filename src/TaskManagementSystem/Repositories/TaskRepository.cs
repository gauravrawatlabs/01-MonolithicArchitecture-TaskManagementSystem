using Microsoft.EntityFrameworkCore;
using TaskManagementSystem.Data;
using TaskManagementSystem.Enums;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Models.DTOs.Tasks;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Repositories;

/// <summary>EF Core implementation of <see cref="ITaskRepository"/>.</summary>
public sealed class TaskRepository(ApplicationDbContext dbContext) : ITaskRepository
{
    public async Task<(IReadOnlyList<TaskResponse> Items, int TotalCount)> GetPagedForUserAsync(
        TaskQueryParameters query, Guid userId, bool isAdmin, DateTime utcNow, CancellationToken cancellationToken)
    {
        var tasks = dbContext.Tasks.AsNoTracking();

        // Row-level visibility: tasks of projects the user owns or is a member of.
        if (!isAdmin)
        {
            tasks = tasks.Where(t => t.Project.OwnerId == userId || t.Project.Members.Any(m => m.UserId == userId));
        }

        if (query.ProjectId is { } projectId)
        {
            tasks = tasks.Where(t => t.ProjectId == projectId);
        }

        if (query.Status is { } status)
        {
            tasks = tasks.Where(t => t.Status == status);
        }

        if (query.Priority is { } priority)
        {
            tasks = tasks.Where(t => t.Priority == priority);
        }

        if (query.AssignedToMe == true)
        {
            tasks = tasks.Where(t => t.AssignedToId == userId);
        }
        else if (query.AssignedToId is { } assignedToId)
        {
            tasks = tasks.Where(t => t.AssignedToId == assignedToId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            tasks = tasks.Where(t => t.Title.Contains(search) || (t.Description != null && t.Description.Contains(search)));
        }

        var totalCount = await tasks.CountAsync(cancellationToken);

        var items = await ProjectToResponse(
                tasks
                    .OrderBy(t => t.DueDateUtc == null)   // tasks with a due date first
                    .ThenBy(t => t.DueDateUtc)
                    .ThenBy(t => t.Id)
                    .Skip((query.Page - 1) * query.PageSize)
                    .Take(query.PageSize),
                utcNow)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<TaskResponse?> GetResponseByIdAsync(int id, DateTime utcNow, CancellationToken cancellationToken) =>
        ProjectToResponse(dbContext.Tasks.AsNoTracking().Where(t => t.Id == id), utcNow)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<TaskItem?> GetByIdWithAccessDataAsync(int id, CancellationToken cancellationToken) =>
        dbContext.Tasks
            .Include(t => t.Project).ThenInclude(p => p.Members)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public void Add(TaskItem task) => dbContext.Tasks.Add(task);

    public void Remove(TaskItem task) => dbContext.Tasks.Remove(task);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// The ONE place that shapes a task into a TaskResponse — used for lists and single items, so they
    /// can never disagree. Runs entirely in SQL (joins for names, IsOverdue computed by the database).
    /// </summary>
    private static IQueryable<TaskResponse> ProjectToResponse(IQueryable<TaskItem> tasks, DateTime utcNow) =>
        tasks.Select(t => new TaskResponse(
            t.Id,
            t.Title,
            t.Description,
            t.Status,
            t.Priority,
            t.DueDateUtc,
            t.CompletedAtUtc,
            t.DueDateUtc != null && t.DueDateUtc < utcNow
                && t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Cancelled,
            t.ProjectId,
            t.Project.Name,
            t.CategoryId,
            t.Category != null ? t.Category.Name : null,
            t.CreatedById,
            t.CreatedBy.FirstName + " " + t.CreatedBy.LastName,
            t.AssignedToId,
            t.AssignedTo != null ? t.AssignedTo.FirstName + " " + t.AssignedTo.LastName : null,
            t.CreatedAtUtc,
            t.UpdatedAtUtc));
}
