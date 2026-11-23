using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TaskManagementSystem.Data;
using TaskManagementSystem.Enums;
using TaskManagementSystem.Helpers;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Models.DTOs.Tasks;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Repositories;

/// <summary>EF Core implementation of <see cref="ITaskRepository"/>.</summary>
public sealed class TaskRepository(ApplicationDbContext dbContext) : ITaskRepository
{
    /// <summary>
    /// Priority as a NUMBER for sorting. Enums are stored as strings, so ORDER BY Priority would be
    /// ALPHABETICAL (Critical, High, Low, Medium). A CASE expression gives the business order.
    /// </summary>
    private static readonly Expression<Func<TaskItem, int>> PriorityRank = t =>
        t.Priority == TaskPriority.Critical ? 4 :
        t.Priority == TaskPriority.High ? 3 :
        t.Priority == TaskPriority.Medium ? 2 : 1;

    /// <summary>Status in workflow order (Todo → InProgress → InReview → Done → Cancelled), not A–Z.</summary>
    private static readonly Expression<Func<TaskItem, int>> StatusRank = t =>
        t.Status == TaskItemStatus.Todo ? 1 :
        t.Status == TaskItemStatus.InProgress ? 2 :
        t.Status == TaskItemStatus.InReview ? 3 :
        t.Status == TaskItemStatus.Done ? 4 : 5;

    public async Task<(IReadOnlyList<TaskResponse> Items, int TotalCount)> GetPagedForUserAsync(
        TaskQueryParameters query, Guid userId, bool isAdmin, DateTime utcNow, CancellationToken cancellationToken)
    {
        var tasks = ApplyFilters(dbContext.Tasks.AsNoTracking().VisibleTo(userId, isAdmin), query, userId, utcNow);

        var totalCount = await tasks.CountAsync(cancellationToken);

        var items = await ProjectToResponse(ApplySorting(tasks, query).ApplyPaging(query), utcNow)
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

    /// <summary>Each filter is optional and only added to the WHERE clause when supplied.</summary>
    private static IQueryable<TaskItem> ApplyFilters(
        IQueryable<TaskItem> tasks, TaskQueryParameters query, Guid userId, DateTime utcNow)
    {
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

        if (query.CategoryId is { } categoryId)
        {
            tasks = tasks.Where(t => t.CategoryId == categoryId);
        }

        if (query.AssignedToMe == true)
        {
            tasks = tasks.Where(t => t.AssignedToId == userId);
        }
        else if (query.AssignedToId is { } assignedToId)
        {
            tasks = tasks.Where(t => t.AssignedToId == assignedToId);
        }

        if (query.DueFrom is { } dueFrom)
        {
            var from = dueFrom.AsUtc();
            tasks = tasks.Where(t => t.DueDateUtc >= from);
        }

        if (query.DueTo is { } dueTo)
        {
            var to = dueTo.AsUtc();
            tasks = tasks.Where(t => t.DueDateUtc < to);
        }

        if (query.Overdue is { } overdue)
        {
            tasks = overdue
                ? tasks.Where(t => t.DueDateUtc != null && t.DueDateUtc < utcNow
                                   && t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Cancelled)
                : tasks.Where(t => t.DueDateUtc == null || t.DueDateUtc >= utcNow
                                   || t.Status == TaskItemStatus.Done || t.Status == TaskItemStatus.Cancelled);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            tasks = tasks.Where(t => t.Title.Contains(search) || (t.Description != null && t.Description.Contains(search)));
        }

        return tasks;
    }

    /// <summary>
    /// Whitelisted sorting. Every branch ends with ThenBy(Id): without a unique tiebreaker, rows with
    /// equal keys may come back in a different order on each query and jump between pages.
    /// </summary>
    private static IOrderedQueryable<TaskItem> ApplySorting(IQueryable<TaskItem> tasks, TaskQueryParameters query)
    {
        var direction = query.SortDirection ?? query.SortBy switch
        {
            TaskSortField.Priority or TaskSortField.CreatedAt or TaskSortField.UpdatedAt => SortDirection.Desc,
            _ => SortDirection.Asc
        };

        var sorted = query.SortBy switch
        {
            // Tasks without a due date go LAST in both directions.
            TaskSortField.DueDate => tasks.OrderBy(t => t.DueDateUtc == null).ThenByDirection(t => t.DueDateUtc, direction),
            TaskSortField.Priority => tasks.OrderByDirection(PriorityRank, direction),
            TaskSortField.Status => tasks.OrderByDirection(StatusRank, direction),
            TaskSortField.Title => tasks.OrderByDirection(t => t.Title, direction),
            TaskSortField.CreatedAt => tasks.OrderByDirection(t => t.CreatedAtUtc, direction),
            TaskSortField.UpdatedAt => tasks.OrderBy(t => t.UpdatedAtUtc == null).ThenByDirection(t => t.UpdatedAtUtc, direction),
            _ => throw new ArgumentOutOfRangeException(nameof(query), query.SortBy, "Unsupported sort field.")
        };

        return sorted.ThenBy(t => t.Id);
    }

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
