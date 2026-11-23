using TaskManagementSystem.Models.DTOs.Tasks;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Interfaces.Repositories;

/// <summary>Data access for tasks.</summary>
public interface ITaskRepository
{
    /// <summary>A page of tasks visible to the user (member projects; Admin: all), projected to DTOs.</summary>
    Task<(IReadOnlyList<TaskResponse> Items, int TotalCount)> GetPagedForUserAsync(
        TaskQueryParameters query, Guid userId, bool isAdmin, DateTime utcNow, CancellationToken cancellationToken);

    /// <summary>One task projected to a DTO (call only after authorization), or null.</summary>
    Task<TaskResponse?> GetResponseByIdAsync(int id, DateTime utcNow, CancellationToken cancellationToken);

    /// <summary>A tracked task with Project + Project.Members loaded (what the authorization handler needs).</summary>
    Task<TaskItem?> GetByIdWithAccessDataAsync(int id, CancellationToken cancellationToken);

    void Add(TaskItem task);

    void Remove(TaskItem task);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
