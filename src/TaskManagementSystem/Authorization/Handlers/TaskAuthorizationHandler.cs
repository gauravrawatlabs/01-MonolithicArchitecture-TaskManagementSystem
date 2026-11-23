using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using TaskManagementSystem.Authorization.Operations;
using TaskManagementSystem.Extensions;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Authorization.Handlers;

/// <summary>
/// Resource-based rules for tasks. Only project members (and Admins) get anything at all:
///
///   Operation      Admin  ProjectOwner  Creator  Assignee  OtherMember
///   Read             ✔        ✔           ✔        ✔          ✔
///   AddComment       ✔        ✔           ✔        ✔          ✔
///   Update           ✔        ✔           ✔        ✘          ✘
///   Delete           ✔        ✔           ✔        ✘          ✘
///   Assign           ✔        ✔           ✘        ✘          ✘
///   ChangeStatus     ✔        ✔           ✔        ✔          ✘
///   Reopen           ✔        ✔           ✘        ✘          ✘
///
/// A creator/assignee who is no longer a member loses all rights (membership is checked first).
/// Requires task.Project and task.Project.Members to be loaded — otherwise it FAILS CLOSED.
/// </summary>
public sealed class TaskAuthorizationHandler
    : AuthorizationHandler<OperationAuthorizationRequirement, TaskItem>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        TaskItem task)
    {
        if (context.User.IsAdmin())
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Navigation not loaded → deny rather than throw or allow.
        if (task.Project is null || !context.User.TryGetUserId(out var userId))
        {
            return Task.CompletedTask;
        }

        var isProjectOwner = task.Project.OwnerId == userId;
        var isMember = isProjectOwner || task.Project.Members.Any(m => m.UserId == userId);
        if (!isMember)
        {
            return Task.CompletedTask;
        }

        var isCreator = task.CreatedById == userId;
        var isAssignee = task.AssignedToId == userId;

        var allowed = requirement.Name switch
        {
            nameof(TaskOperations.Read) => true,
            nameof(TaskOperations.AddComment) => true,
            nameof(TaskOperations.Update) => isProjectOwner || isCreator,
            nameof(TaskOperations.Delete) => isProjectOwner || isCreator,
            nameof(TaskOperations.Assign) => isProjectOwner,
            nameof(TaskOperations.ChangeStatus) => isProjectOwner || isCreator || isAssignee,
            nameof(TaskOperations.Reopen) => isProjectOwner,
            _ => false
        };

        if (allowed)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
