namespace TaskManagementSystem.Enums;

/// <summary>
/// Workflow state of a task. Named TaskItemStatus (not TaskStatus) to avoid clashing
/// with System.Threading.Tasks.TaskStatus. Stored as a string in the database.
/// </summary>
public enum TaskItemStatus
{
    Todo,
    InProgress,
    InReview,
    Done,
    Cancelled
}
