using TaskManagementSystem.Enums;

namespace TaskManagementSystem.Services;

/// <summary>
/// The task status state machine — a pure business rule with no dependencies (easy to unit test).
///
///   Todo ⇄ InProgress ⇄ InReview → Done
///   Todo / InProgress / InReview → Cancelled
///   Done → InProgress        (reopen,  owner/Admin only)
///   Cancelled → Todo         (restore, owner/Admin only)
///
/// Any transition not listed is invalid (→ 409 Conflict).
/// </summary>
public static class TaskStatusWorkflow
{
    // (from, to) → requires project owner / Admin?
    private static readonly Dictionary<(TaskItemStatus From, TaskItemStatus To), bool> Transitions = new()
    {
        [(TaskItemStatus.Todo, TaskItemStatus.InProgress)] = false,
        [(TaskItemStatus.Todo, TaskItemStatus.Cancelled)] = false,

        [(TaskItemStatus.InProgress, TaskItemStatus.Todo)] = false,
        [(TaskItemStatus.InProgress, TaskItemStatus.InReview)] = false,
        [(TaskItemStatus.InProgress, TaskItemStatus.Cancelled)] = false,

        [(TaskItemStatus.InReview, TaskItemStatus.InProgress)] = false,
        [(TaskItemStatus.InReview, TaskItemStatus.Done)] = false,
        [(TaskItemStatus.InReview, TaskItemStatus.Cancelled)] = false,

        [(TaskItemStatus.Done, TaskItemStatus.InProgress)] = true,
        [(TaskItemStatus.Cancelled, TaskItemStatus.Todo)] = true
    };

    /// <summary>True if moving from <paramref name="from"/> to <paramref name="to"/> is allowed.</summary>
    /// <param name="from">Current status.</param>
    /// <param name="to">Requested status.</param>
    /// <param name="requiresProjectOwner">True for reopen/restore transitions (owner or Admin only).</param>
    public static bool CanTransition(TaskItemStatus from, TaskItemStatus to, out bool requiresProjectOwner) =>
        Transitions.TryGetValue((from, to), out requiresProjectOwner);

    /// <summary>The statuses reachable from <paramref name="from"/> (for error messages and UIs).</summary>
    public static IReadOnlyList<TaskItemStatus> GetNextStatuses(TaskItemStatus from) =>
        Transitions.Keys.Where(t => t.From == from).Select(t => t.To).ToList();

    /// <summary>Done and Cancelled are "closed": they can't be overdue.</summary>
    public static bool IsClosed(TaskItemStatus status) =>
        status is TaskItemStatus.Done or TaskItemStatus.Cancelled;
}
