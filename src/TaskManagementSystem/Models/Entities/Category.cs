namespace TaskManagementSystem.Models.Entities;

/// <summary>Global task category (e.g. Bug, Feature). Managed by Admins, readable by everyone.</summary>
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<TaskItem> Tasks { get; set; } = [];
}
