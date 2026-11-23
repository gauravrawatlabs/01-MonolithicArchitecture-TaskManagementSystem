using TaskManagementSystem.Enums;
using TaskManagementSystem.Models.DTOs.Common;

namespace TaskManagementSystem.Models.DTOs.Tasks;

/// <summary>A task as returned by the API.</summary>
/// <param name="Id">Task id.</param>
/// <param name="Title">Title.</param>
/// <param name="Description">Optional description.</param>
/// <param name="Status">Todo, InProgress, InReview, Done or Cancelled.</param>
/// <param name="Priority">Low, Medium, High or Critical.</param>
/// <param name="DueDateUtc">Optional due date/time (UTC).</param>
/// <param name="CompletedAtUtc">When the task was moved to Done (UTC), otherwise null.</param>
/// <param name="IsOverdue">True if the due date has passed and the task is not Done/Cancelled.</param>
/// <param name="ProjectId">The project the task belongs to.</param>
/// <param name="ProjectName">The project's name.</param>
/// <param name="CategoryId">Optional category id.</param>
/// <param name="CategoryName">Optional category name.</param>
/// <param name="CreatedById">Who created the task.</param>
/// <param name="CreatedByName">Creator's full name.</param>
/// <param name="AssignedToId">Assignee's user id, or null if unassigned.</param>
/// <param name="AssignedToName">Assignee's full name, or null.</param>
/// <param name="CreatedAtUtc">Creation time (UTC).</param>
/// <param name="UpdatedAtUtc">Last update time (UTC), or null.</param>
public sealed record TaskResponse(
    int Id,
    string Title,
    string? Description,
    TaskItemStatus Status,
    TaskPriority Priority,
    DateTime? DueDateUtc,
    DateTime? CompletedAtUtc,
    bool IsOverdue,
    int ProjectId,
    string ProjectName,
    int? CategoryId,
    string? CategoryName,
    Guid CreatedById,
    string CreatedByName,
    Guid? AssignedToId,
    string? AssignedToName,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

/// <summary>Data for creating a task. New tasks start as Todo and unassigned.</summary>
/// <param name="ProjectId">The project to create the task in (you must be a member).</param>
/// <param name="Title">Title (max 200 characters).</param>
/// <param name="Description">Optional description (max 4000 characters).</param>
/// <param name="Priority">Low, Medium (default), High or Critical.</param>
/// <param name="DueDateUtc">Optional due date in the future, e.g. "2026-12-31T17:00:00Z". Must not be after the project's end date.</param>
/// <param name="CategoryId">Optional category id.</param>
public sealed record CreateTaskRequest(
    int ProjectId,
    string Title,
    string? Description,
    TaskPriority? Priority,
    DateTime? DueDateUtc,
    int? CategoryId);

/// <summary>Data for updating a task's details (replaces all editable fields; status and assignee have their own endpoints).</summary>
/// <param name="Title">Title (max 200 characters).</param>
/// <param name="Description">Optional description (max 4000 characters).</param>
/// <param name="Priority">Low, Medium, High or Critical.</param>
/// <param name="DueDateUtc">Optional due date. A CHANGED due date must be in the future.</param>
/// <param name="CategoryId">Optional category id (null removes the category).</param>
public sealed record UpdateTaskRequest(
    string Title,
    string? Description,
    TaskPriority Priority,
    DateTime? DueDateUtc,
    int? CategoryId);

/// <summary>Assigns or unassigns a task.</summary>
/// <param name="AssignedToId">A project member's user id, or null to unassign.</param>
public sealed record AssignTaskRequest(Guid? AssignedToId);

/// <summary>Moves a task to another status (see the workflow rules).</summary>
/// <param name="Status">Todo, InProgress, InReview, Done or Cancelled.</param>
public sealed record ChangeTaskStatusRequest(TaskItemStatus Status);

/// <summary>Search, filters, sorting and paging for the task list (bound from the query string).</summary>
public sealed class TaskQueryParameters : PagedQueryParameters
{
    /// <summary>Only tasks of this project.</summary>
    public int? ProjectId { get; init; }

    /// <summary>Only tasks with this status.</summary>
    public TaskItemStatus? Status { get; init; }

    /// <summary>Only tasks with this priority.</summary>
    public TaskPriority? Priority { get; init; }

    /// <summary>Only tasks in this category.</summary>
    public int? CategoryId { get; init; }

    /// <summary>Only tasks assigned to this user.</summary>
    public Guid? AssignedToId { get; init; }

    /// <summary>true = only tasks assigned to me.</summary>
    public bool? AssignedToMe { get; init; }

    /// <summary>Only tasks due at or after this time (UTC), e.g. 2026-12-01T00:00:00Z.</summary>
    public DateTime? DueFrom { get; init; }

    /// <summary>Only tasks due before this time (UTC).</summary>
    public DateTime? DueTo { get; init; }

    /// <summary>true = only overdue tasks (due date passed, not Done/Cancelled); false = only not overdue.</summary>
    public bool? Overdue { get; init; }

    /// <summary>Matches title or description (contains).</summary>
    public string? Search { get; init; }

    /// <summary>DueDate (default), Priority, Status, Title, CreatedAt or UpdatedAt.</summary>
    public TaskSortField SortBy { get; init; } = TaskSortField.DueDate;

    /// <summary>
    /// Asc or Desc. Default: Asc for DueDate/Title/Status, Desc for Priority (most urgent first)
    /// and CreatedAt/UpdatedAt (newest first).
    /// </summary>
    public SortDirection? SortDirection { get; init; }
}
