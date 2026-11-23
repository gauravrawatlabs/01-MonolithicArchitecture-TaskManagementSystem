using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Repositories;

/// <summary>
/// ROW-LEVEL SECURITY in one place. "Which projects/tasks may this user see?" is answered here,
/// and every query that returns or aggregates them (lists, dashboard) starts from these filters —
/// so a list and the dashboard can never disagree about what a user is allowed to see.
///
/// Rule: Admins see everything; everyone else sees projects they own or are a member of,
/// and the tasks inside those projects.
/// </summary>
public static class QueryFilters
{
    public static IQueryable<Project> VisibleTo(this IQueryable<Project> projects, Guid userId, bool isAdmin) =>
        isAdmin
            ? projects
            : projects.Where(p => p.OwnerId == userId || p.Members.Any(m => m.UserId == userId));

    public static IQueryable<TaskItem> VisibleTo(this IQueryable<TaskItem> tasks, Guid userId, bool isAdmin) =>
        isAdmin
            ? tasks
            : tasks.Where(t => t.Project.OwnerId == userId || t.Project.Members.Any(m => m.UserId == userId));
}
