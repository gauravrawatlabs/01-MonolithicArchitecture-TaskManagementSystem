using TaskManagementSystem.Enums;
using TaskManagementSystem.Models.DTOs.Dashboard;

namespace TaskManagementSystem.Interfaces.Repositories;

/// <summary>Raw counters for the dashboard (before percentages / zero-filling).</summary>
public sealed record TaskCounters(
    int Total, int Open, int Overdue, int DueInNext7Days, int CompletedInLast7Days,
    int MyOpen, int MyOverdue, int MyDueInNext7Days);

/// <summary>
/// Aggregate (read-only) queries for the dashboard. Every query starts from the same
/// row-level visibility filter as the list endpoints (QueryFilters.VisibleTo).
/// </summary>
public interface IDashboardRepository
{
    Task<int> CountProjectsAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken);

    Task<TaskCounters> GetTaskCountersAsync(Guid userId, bool isAdmin, DateTime utcNow, CancellationToken cancellationToken);

    Task<Dictionary<TaskItemStatus, int>> CountByStatusAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken);

    Task<Dictionary<TaskPriority, int>> CountByPriorityAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken);

    /// <summary>Project progress rows (CompletionPercent not yet calculated), most open tasks first.</summary>
    Task<IReadOnlyList<ProjectProgressResponse>> GetProjectProgressAsync(
        Guid userId, bool isAdmin, DateTime utcNow, int take, CancellationToken cancellationToken);
}
