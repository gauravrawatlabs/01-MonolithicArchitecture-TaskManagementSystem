using TaskManagementSystem.Enums;

namespace TaskManagementSystem.Models.DTOs.Dashboard;

/// <summary>Statistics for the current user's scope (Admin: everything; others: projects they belong to).</summary>
/// <param name="GeneratedAtUtc">When the numbers were calculated (UTC).</param>
/// <param name="Scope">"AllProjects" for Admins, otherwise "MyProjects".</param>
/// <param name="ProjectCount">Projects in scope.</param>
/// <param name="Tasks">Task totals in scope.</param>
/// <param name="MyWork">Tasks assigned to the current user.</param>
/// <param name="TasksByStatus">Task count per status (every status present, zeros included).</param>
/// <param name="TasksByPriority">Task count per priority (every priority present, zeros included).</param>
/// <param name="Projects">Progress of up to 10 projects with the most open tasks.</param>
public sealed record DashboardResponse(
    DateTime GeneratedAtUtc,
    string Scope,
    int ProjectCount,
    TaskTotals Tasks,
    MyWorkSummary MyWork,
    IReadOnlyDictionary<TaskItemStatus, int> TasksByStatus,
    IReadOnlyDictionary<TaskPriority, int> TasksByPriority,
    IReadOnlyList<ProjectProgressResponse> Projects);

/// <summary>Task totals.</summary>
/// <param name="Total">All tasks.</param>
/// <param name="Open">Not Done and not Cancelled.</param>
/// <param name="Overdue">Open tasks whose due date has passed.</param>
/// <param name="DueInNext7Days">Open tasks due within the next 7 days.</param>
/// <param name="CompletedInLast7Days">Tasks moved to Done within the last 7 days.</param>
public sealed record TaskTotals(int Total, int Open, int Overdue, int DueInNext7Days, int CompletedInLast7Days);

/// <summary>The current user's assigned work.</summary>
/// <param name="AssignedOpen">Open tasks assigned to me.</param>
/// <param name="AssignedOverdue">My open tasks that are overdue.</param>
/// <param name="AssignedDueInNext7Days">My open tasks due within the next 7 days.</param>
public sealed record MyWorkSummary(int AssignedOpen, int AssignedOverdue, int AssignedDueInNext7Days);

/// <summary>Progress of one project.</summary>
/// <param name="ProjectId">Project id.</param>
/// <param name="Name">Project name.</param>
/// <param name="TotalTasks">All tasks.</param>
/// <param name="OpenTasks">Not Done / Cancelled.</param>
/// <param name="DoneTasks">Done.</param>
/// <param name="OverdueTasks">Open and past due.</param>
/// <param name="CompletionPercent">Done ÷ (Total − Cancelled) × 100, rounded to 1 decimal; 0 when there is nothing to do.</param>
public sealed record ProjectProgressResponse(
    int ProjectId,
    string Name,
    int TotalTasks,
    int OpenTasks,
    int DoneTasks,
    int OverdueTasks,
    double CompletionPercent);
