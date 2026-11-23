using System.Security.Claims;
using TaskManagementSystem.Models.DTOs.Common;
using TaskManagementSystem.Models.DTOs.Tasks;

namespace TaskManagementSystem.Interfaces.Services;

/// <summary>
/// Task business operations. 404 when the caller can't see the task/project,
/// 403 when they can see it but may not perform the operation.
/// </summary>
public interface ITaskService
{
    Task<PagedResult<TaskResponse>> GetTasksAsync(
        TaskQueryParameters query, ClaimsPrincipal currentUser, CancellationToken cancellationToken);

    Task<TaskResponse> GetByIdAsync(int id, ClaimsPrincipal currentUser, CancellationToken cancellationToken);

    /// <exception cref="Exceptions.BusinessRuleException">Unknown category, or due date after the project end date.</exception>
    Task<TaskResponse> CreateAsync(CreateTaskRequest request, ClaimsPrincipal currentUser, CancellationToken cancellationToken);

    Task<TaskResponse> UpdateAsync(
        int id, UpdateTaskRequest request, ClaimsPrincipal currentUser, CancellationToken cancellationToken);

    /// <exception cref="Exceptions.BusinessRuleException">The assignee is not an active project member.</exception>
    Task<TaskResponse> AssignAsync(
        int id, AssignTaskRequest request, ClaimsPrincipal currentUser, CancellationToken cancellationToken);

    /// <exception cref="Exceptions.ConflictException">The workflow does not allow this transition.</exception>
    Task<TaskResponse> ChangeStatusAsync(
        int id, ChangeTaskStatusRequest request, ClaimsPrincipal currentUser, CancellationToken cancellationToken);

    Task DeleteAsync(int id, ClaimsPrincipal currentUser, CancellationToken cancellationToken);
}
