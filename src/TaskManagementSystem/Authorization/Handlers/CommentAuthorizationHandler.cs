using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using TaskManagementSystem.Authorization.Operations;
using TaskManagementSystem.Extensions;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Authorization.Handlers;

/// <summary>
/// Resource-based rules for comments:
///   Update → the AUTHOR only (nobody may put words in someone else's mouth — not even an Admin)
///   Delete → the author, the project owner (moderation) or an Admin
/// The author must still be a project member. Requires comment.Task.Project.Members loaded (fails closed).
/// </summary>
public sealed class CommentAuthorizationHandler
    : AuthorizationHandler<OperationAuthorizationRequirement, Comment>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        Comment comment)
    {
        var project = comment.Task?.Project;
        if (project is null || !context.User.TryGetUserId(out var userId))
        {
            return Task.CompletedTask;
        }

        var isAdmin = context.User.IsAdmin();
        var isProjectOwner = project.OwnerId == userId;
        var isMember = isProjectOwner || project.Members.Any(m => m.UserId == userId);
        var isAuthor = comment.AuthorId == userId && isMember;

        var allowed = requirement.Name switch
        {
            nameof(CommentOperations.Update) => isAuthor,
            nameof(CommentOperations.Delete) => isAuthor || isProjectOwner || isAdmin,
            _ => false
        };

        if (allowed)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
