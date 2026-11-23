using TaskManagementSystem.Enums;

namespace TaskManagementSystem.Models.Entities;

/// <summary>
/// A task inside a project. Named TaskItem (table: Tasks) to avoid clashing with
/// System.Threading.Tasks.Task, which is used in every async method.
/// </summary>
public class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TaskItemStatus Status { get; set; } = TaskItemStatus.Todo;
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public DateTime? DueDateUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public int ProjectId { get; set; }
    public int? CategoryId { get; set; }
    public Guid CreatedById { get; set; }
    public Guid? AssignedToId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public Project Project { get; set; } = null!;
    public Category? Category { get; set; }
    public ApplicationUser CreatedBy { get; set; } = null!;
    public ApplicationUser? AssignedTo { get; set; }
    public ICollection<Comment> Comments { get; set; } = [];
}
