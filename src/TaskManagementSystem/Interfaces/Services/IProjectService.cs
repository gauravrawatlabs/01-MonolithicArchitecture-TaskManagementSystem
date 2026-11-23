using System.Security.Claims;
using TaskManagementSystem.Models.DTOs.Common;
using TaskManagementSystem.Models.DTOs.Projects;

namespace TaskManagementSystem.Interfaces.Services;

/// <summary>
/// Project business operations. Every method receives the current user and applies
/// resource-based authorization: 404 if the user may not see the project, 403 if they may
/// see it but not perform the operation.
/// </summary>
public interface IProjectService
{
    Task<PagedResult<ProjectSummaryResponse>> GetProjectsAsync(
        ProjectQueryParameters query, ClaimsPrincipal currentUser, CancellationToken cancellationToken);

    Task<ProjectDetailsResponse> GetByIdAsync(int id, ClaimsPrincipal currentUser, CancellationToken cancellationToken);

    /// <exception cref="Exceptions.ConflictException">The caller already owns a project with this name.</exception>
    Task<ProjectDetailsResponse> CreateAsync(
        CreateProjectRequest request, ClaimsPrincipal currentUser, CancellationToken cancellationToken);

    Task<ProjectDetailsResponse> UpdateAsync(
        int id, UpdateProjectRequest request, ClaimsPrincipal currentUser, CancellationToken cancellationToken);

    /// <summary>Deletes the project and (cascade) its tasks, comments and memberships.</summary>
    Task DeleteAsync(int id, ClaimsPrincipal currentUser, CancellationToken cancellationToken);

    /// <exception cref="Exceptions.ConflictException">Already a member.</exception>
    /// <exception cref="Exceptions.BusinessRuleException">The user doesn't exist or is inactive.</exception>
    Task<ProjectDetailsResponse> AddMemberAsync(
        int id, AddProjectMemberRequest request, ClaimsPrincipal currentUser, CancellationToken cancellationToken);

    /// <summary>Removes a member and unassigns their tasks in this project.</summary>
    /// <exception cref="Exceptions.BusinessRuleException">Trying to remove the owner.</exception>
    Task RemoveMemberAsync(int id, Guid userId, ClaimsPrincipal currentUser, CancellationToken cancellationToken);
}
