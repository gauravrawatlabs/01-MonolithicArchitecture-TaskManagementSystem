using Microsoft.EntityFrameworkCore;
using TaskManagementSystem.Data;
using TaskManagementSystem.Enums;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Models.DTOs.Dashboard;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Repositories;

/// <summary>
/// Dashboard aggregates computed BY THE DATABASE: COUNT / CASE / GROUP BY in SQL, so no task rows are
/// loaded into memory — the cost stays roughly constant whether there are 10 or 1,000,000 tasks.
/// </summary>
public sealed class DashboardRepository(ApplicationDbContext dbContext) : IDashboardRepository
{
    public Task<int> CountProjectsAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken) =>
        dbContext.Projects.AsNoTracking().VisibleTo(userId, isAdmin).CountAsync(cancellationToken);

    public async Task<TaskCounters> GetTaskCountersAsync(
        Guid userId, bool isAdmin, DateTime utcNow, CancellationToken cancellationToken)
    {
        var in7Days = utcNow.AddDays(7);
        var sevenDaysAgo = utcNow.AddDays(-7);

        // GroupBy a constant = "one group containing every row". Each g.Count(condition) becomes
        // COUNT(CASE WHEN condition THEN 1 END) — eight counters in ONE SQL statement.
        var counters = await Tasks(userId, isAdmin)
            .GroupBy(_ => 1)
            .Select(g => new TaskCounters(
                g.Count(),
                g.Count(t => t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Cancelled),
                g.Count(t => t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Cancelled
                             && t.DueDateUtc != null && t.DueDateUtc < utcNow),
                g.Count(t => t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Cancelled
                             && t.DueDateUtc >= utcNow && t.DueDateUtc < in7Days),
                g.Count(t => t.Status == TaskItemStatus.Done && t.CompletedAtUtc >= sevenDaysAgo),
                g.Count(t => t.AssignedToId == userId
                             && t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Cancelled),
                g.Count(t => t.AssignedToId == userId
                             && t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Cancelled
                             && t.DueDateUtc != null && t.DueDateUtc < utcNow),
                g.Count(t => t.AssignedToId == userId
                             && t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Cancelled
                             && t.DueDateUtc >= utcNow && t.DueDateUtc < in7Days)))
            .FirstOrDefaultAsync(cancellationToken);

        // No tasks at all → no group → null.
        return counters ?? new TaskCounters(0, 0, 0, 0, 0, 0, 0, 0);
    }

    public Task<Dictionary<TaskItemStatus, int>> CountByStatusAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken) =>
        Tasks(userId, isAdmin)
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);

    public Task<Dictionary<TaskPriority, int>> CountByPriorityAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken) =>
        Tasks(userId, isAdmin)
            .GroupBy(t => t.Priority)
            .Select(g => new { Priority = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Priority, x => x.Count, cancellationToken);

    public async Task<IReadOnlyList<ProjectProgressResponse>> GetProjectProgressAsync(
        Guid userId, bool isAdmin, DateTime utcNow, int take, CancellationToken cancellationToken)
    {
        // Per-project counters as correlated COUNT sub-queries, top N by open tasks.
        var rows = await dbContext.Projects.AsNoTracking()
            .VisibleTo(userId, isAdmin)
            .Select(p => new
            {
                p.Id,
                p.Name,
                Total = p.Tasks.Count(),
                Open = p.Tasks.Count(t => t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Cancelled),
                Done = p.Tasks.Count(t => t.Status == TaskItemStatus.Done),
                Cancelled = p.Tasks.Count(t => t.Status == TaskItemStatus.Cancelled),
                Overdue = p.Tasks.Count(t => t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Cancelled
                                             && t.DueDateUtc != null && t.DueDateUtc < utcNow)
            })
            .OrderByDescending(x => x.Open)
            .ThenBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        // The percentage is presentation math → done in C# on the 10 small rows.
        return rows
            .Select(x => new ProjectProgressResponse(
                x.Id,
                x.Name,
                x.Total,
                x.Open,
                x.Done,
                x.Overdue,
                x.Total - x.Cancelled == 0 ? 0 : Math.Round(100.0 * x.Done / (x.Total - x.Cancelled), 1)))
            .ToList();
    }

    private IQueryable<TaskItem> Tasks(Guid userId, bool isAdmin) =>
        dbContext.Tasks.AsNoTracking().VisibleTo(userId, isAdmin);
}
