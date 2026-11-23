using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Identity;
using TaskManagementSystem.Authorization.Operations;
using TaskManagementSystem.Enums;
using TaskManagementSystem.Exceptions;
using TaskManagementSystem.Extensions;
using TaskManagementSystem.Helpers;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Models.DTOs.Common;
using TaskManagementSystem.Models.DTOs.Tasks;
using TaskManagementSystem.Models.Entities;
using TaskManagementSystem.Validators.Tasks;

namespace TaskManagementSystem.Services;

/// <summary>
/// Task business logic. Note how the monolith's modules cooperate through plain in-process calls:
/// this service uses the PROJECT repository (membership), the CATEGORY repository (category exists)
/// and Identity's UserManager (assignee is active) — no HTTP calls, one database, one transaction scope.
/// </summary>
public sealed class TaskService(
    ITaskRepository taskRepository,
    IProjectRepository projectRepository,
    ICategoryRepository categoryRepository,
    UserManager<ApplicationUser> userManager,
    IAuthorizationService authorizationService,
    TimeProvider timeProvider,
    ILogger<TaskService> logger) : ITaskService
{
    private const string ResourceName = "Task";

    public async Task<PagedResult<TaskResponse>> GetTasksAsync(
        TaskQueryParameters query, ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await taskRepository.GetPagedForUserAsync(
            query, currentUser.GetUserId(), currentUser.IsAdmin(), UtcNow(), cancellationToken);

        return new PagedResult<TaskResponse>(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<TaskResponse> GetByIdAsync(int id, ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        await GetAuthorizedTaskAsync(id, currentUser, TaskOperations.Read, cancellationToken);
        return await GetResponseAsync(id, cancellationToken);
    }

    public async Task<TaskResponse> CreateAsync(
        CreateTaskRequest request, ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        // Creating a task = writing into a project → the caller must be able to see (be a member of) it.
        var projectNotFound = NotFoundException.For("Project", request.ProjectId);
        var project = await projectRepository.GetByIdWithMembersAsync(request.ProjectId, cancellationToken)
                      ?? throw projectNotFound;
        await authorizationService.AuthorizeOrNotFoundAsync(currentUser, project, ProjectOperations.Read, projectNotFound);

        await EnsureCategoryExistsAsync(request.CategoryId, cancellationToken);
        var dueDateUtc = request.DueDateUtc?.AsUtc();
        EnsureDueDateWithinProject(dueDateUtc, project);

        var task = new TaskItem
        {
            ProjectId = project.Id,
            Title = request.Title.Trim(),
            Description = NormalizeOptional(request.Description),
            Priority = request.Priority ?? TaskPriority.Medium,
            Status = TaskItemStatus.Todo,
            DueDateUtc = dueDateUtc,
            CategoryId = request.CategoryId,
            CreatedById = currentUser.GetUserId(),
            CreatedAtUtc = UtcNow()
        };

        taskRepository.Add(task);
        await taskRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Task {TaskId} created in project {ProjectId} by user {UserId}",
            task.Id, project.Id, task.CreatedById);
        return await GetResponseAsync(task.Id, cancellationToken);
    }

    public async Task<TaskResponse> UpdateAsync(
        int id, UpdateTaskRequest request, ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        var task = await GetAuthorizedTaskAsync(id, currentUser, TaskOperations.Update, cancellationToken);

        await EnsureCategoryExistsAsync(request.CategoryId, cancellationToken);

        var dueDateUtc = request.DueDateUtc?.AsUtc();
        if (dueDateUtc != task.DueDateUtc)
        {
            // Only a CHANGED due date must be in the future; an overdue task stays editable.
            if (dueDateUtc is not null && dueDateUtc <= UtcNow())
            {
                throw new RequestValidationException(new Dictionary<string, string[]>
                {
                    ["dueDateUtc"] = [TaskRules.DueDateInPastMessage]
                });
            }
            EnsureDueDateWithinProject(dueDateUtc, task.Project);
        }

        task.Title = request.Title.Trim();
        task.Description = NormalizeOptional(request.Description);
        task.Priority = request.Priority;
        task.DueDateUtc = dueDateUtc;
        task.CategoryId = request.CategoryId;
        task.UpdatedAtUtc = UtcNow();

        await taskRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Task {TaskId} updated by user {UserId}", id, currentUser.GetUserId());
        return await GetResponseAsync(id, cancellationToken);
    }

    public async Task<TaskResponse> AssignAsync(
        int id, AssignTaskRequest request, ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        var task = await GetAuthorizedTaskAsync(id, currentUser, TaskOperations.Assign, cancellationToken);

        if (request.AssignedToId is { } assigneeId)
        {
            // Business rule: tasks can only be assigned to ACTIVE MEMBERS of the task's project.
            var isMember = task.Project.OwnerId == assigneeId || task.Project.Members.Any(m => m.UserId == assigneeId);
            var assignee = isMember ? await userManager.FindByIdAsync(assigneeId.ToString()) : null;
            if (assignee is null || !assignee.IsActive)
            {
                throw new BusinessRuleException("A task can only be assigned to an active member of its project.");
            }
        }

        if (task.AssignedToId != request.AssignedToId)
        {
            task.AssignedToId = request.AssignedToId;
            task.UpdatedAtUtc = UtcNow();
            await taskRepository.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Task {TaskId} assigned to {AssigneeId} by user {UserId}",
                id, request.AssignedToId, currentUser.GetUserId());
        }

        return await GetResponseAsync(id, cancellationToken);
    }

    public async Task<TaskResponse> ChangeStatusAsync(
        int id, ChangeTaskStatusRequest request, ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        var task = await GetAuthorizedTaskAsync(id, currentUser, TaskOperations.ChangeStatus, cancellationToken);
        var from = task.Status;
        var to = request.Status;

        if (from == to)
        {
            return await GetResponseAsync(id, cancellationToken); // idempotent: nothing to change
        }

        if (!TaskStatusWorkflow.CanTransition(from, to, out var requiresProjectOwner))
        {
            var allowed = string.Join(", ", TaskStatusWorkflow.GetNextStatuses(from));
            throw new ConflictException($"A task cannot move from {from} to {to}. Allowed next statuses: {allowed}.");
        }

        if (requiresProjectOwner)
        {
            // Reopening Done / restoring Cancelled is a management decision.
            var result = await authorizationService.AuthorizeAsync(currentUser, task, TaskOperations.Reopen);
            if (!result.Succeeded)
            {
                throw new ForbiddenException($"Only the project owner or an Admin can move a task from {from} to {to}.");
            }
        }

        task.Status = to;
        task.CompletedAtUtc = to == TaskItemStatus.Done ? UtcNow() : null;
        task.UpdatedAtUtc = UtcNow();
        await taskRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Task {TaskId} status {From} -> {To} by user {UserId}", id, from, to, currentUser.GetUserId());
        return await GetResponseAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(int id, ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        var task = await GetAuthorizedTaskAsync(id, currentUser, TaskOperations.Delete, cancellationToken);

        taskRepository.Remove(task); // comments are cascade-deleted by SQL Server
        await taskRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Task {TaskId} deleted by user {UserId}", id, currentUser.GetUserId());
    }

    /// <summary>Loads the task with its project/members; 404 if not visible, 403 if the operation isn't allowed.</summary>
    private async Task<TaskItem> GetAuthorizedTaskAsync(
        int id, ClaimsPrincipal currentUser, OperationAuthorizationRequirement operation, CancellationToken cancellationToken)
    {
        var notFound = NotFoundException.For(ResourceName, id);
        var task = await taskRepository.GetByIdWithAccessDataAsync(id, cancellationToken) ?? throw notFound;

        await authorizationService.AuthorizeOrNotFoundAsync(currentUser, task, TaskOperations.Read, notFound);

        if (operation != TaskOperations.Read)
        {
            await authorizationService.AuthorizeOrThrowAsync(currentUser, task, operation);
        }

        return task;
    }

    private async Task<TaskResponse> GetResponseAsync(int id, CancellationToken cancellationToken) =>
        await taskRepository.GetResponseByIdAsync(id, UtcNow(), cancellationToken)
        ?? throw NotFoundException.For(ResourceName, id);

    private async Task EnsureCategoryExistsAsync(int? categoryId, CancellationToken cancellationToken)
    {
        if (categoryId is { } id && await categoryRepository.GetByIdAsync(id, cancellationToken) is null)
        {
            throw new BusinessRuleException($"Category {id} does not exist.");
        }
    }

    /// <summary>Business rule: a task can't be due after its project ends.</summary>
    private static void EnsureDueDateWithinProject(DateTime? dueDateUtc, Project project)
    {
        if (dueDateUtc is { } due && project.EndDate is { } endDate && DateOnly.FromDateTime(due) > endDate)
        {
            throw new BusinessRuleException(
                $"The due date must not be after the project's end date ({endDate:yyyy-MM-dd}).");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
