using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using TaskManagementSystem.Authorization.Operations;
using TaskManagementSystem.Extensions;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Authorization.Handlers;

/// <summary>
/// RESOURCE-BASED authorization for user accounts: the decision depends on the specific account
/// being accessed, which an attribute like [Authorize(Roles = ...)] cannot know — the resource is
/// only available after it has been loaded from the database inside the service.
///
/// Rules:
///   Admin → may read any account.
///   Anyone else → may read only their OWN account.
///
/// The handler never calls Fail(): not calling Succeed() is enough to deny, and leaves room for
/// other handlers to grant access. Registered as a SINGLETON: pure logic, no dependencies.
/// </summary>
public sealed class UserAuthorizationHandler
    : AuthorizationHandler<OperationAuthorizationRequirement, ApplicationUser>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        ApplicationUser resource)
    {
        if (requirement.Name != UserOperations.Read.Name)
        {
            return Task.CompletedTask; // unknown operation → not granted
        }

        var isOwner = context.User.TryGetUserId(out var currentUserId) && currentUserId == resource.Id;

        if (context.User.IsAdmin() || isOwner)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
