namespace TaskManagementSystem.Models.Entities;

/// <summary>A comment written by a user on a task.</summary>
public class Comment
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public Guid AuthorId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public TaskItem Task { get; set; } = null!;
    public ApplicationUser Author { get; set; } = null!;
}
