using TaskManagementSystem.Models.DTOs.Projects;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Interfaces.Repositories;

/// <summary>Data access for projects and their members.</summary>
public interface IProjectRepository
{
    /// <summary>
    /// A page of projects VISIBLE to the user (owner or member), or all projects for an Admin.
    /// Visibility is applied in the SQL query — users never receive rows they may not see.
    /// </summary>
    Task<(IReadOnlyList<ProjectSummaryResponse> Items, int TotalCount)> GetPagedForUserAsync(
        ProjectQueryParameters query, Guid userId, bool isAdmin, CancellationToken cancellationToken);

    /// <summary>A tracked project with Owner and Members (+ each member's User) loaded, or null.</summary>
    Task<Project?> GetByIdWithMembersAsync(int id, CancellationToken cancellationToken);

    /// <summary>True if the owner already has another project with this name.</summary>
    Task<bool> NameExistsForOwnerAsync(Guid ownerId, string name, int? excludeId, CancellationToken cancellationToken);

    /// <summary>Clears the assignee of the user's tasks in this project (used when a member is removed).</summary>
    Task<int> UnassignTasksAsync(int projectId, Guid userId, CancellationToken cancellationToken);

    void Add(Project project);

    void Remove(Project project);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
