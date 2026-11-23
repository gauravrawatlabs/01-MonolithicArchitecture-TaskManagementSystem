using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using TaskManagementSystem.Authorization.Operations;
using TaskManagementSystem.Extensions;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Authorization.Handlers;

/// <summary>
/// Resource-based rules for projects:
///
///   Operation       Admin   Owner   Member   Others
///   Read             ✔       ✔       ✔        ✘
///   Update           ✔       ✔       ✘        ✘
///   Delete           ✔       ✔       ✘        ✘
///   ManageMembers    ✔       ✔       ✘        ✘
///
/// IMPORTANT: membership is read from project.Members, so the caller must load Members.
/// If they aren't loaded the collection is empty and members are DENIED — the handler fails closed.
/// Singleton: pure logic, no dependencies (no database access inside the handler).
/// </summary>
public sealed class ProjectAuthorizationHandler
    : AuthorizationHandler<OperationAuthorizationRequirement, Project>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        Project project)
    {
        if (context.User.IsAdmin())
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (!context.User.TryGetUserId(out var userId))
        {
            return Task.CompletedTask;
        }

        var isOwner = project.OwnerId == userId;
        var isMember = isOwner || project.Members.Any(member => member.UserId == userId);

        var allowed = requirement.Name switch
        {
            nameof(ProjectOperations.Read) => isMember,
            nameof(ProjectOperations.Update) => isOwner,
            nameof(ProjectOperations.Delete) => isOwner,
            nameof(ProjectOperations.ManageMembers) => isOwner,
            _ => false
        };

        if (allowed)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
