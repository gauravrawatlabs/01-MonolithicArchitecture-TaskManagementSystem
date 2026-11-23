using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using TaskManagementSystem.Authorization.Operations;
using TaskManagementSystem.Exceptions;
using TaskManagementSystem.Extensions;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Models.DTOs.Comments;
using TaskManagementSystem.Models.DTOs.Common;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Services;

/// <summary>Comment business logic; authorization delegated to Task/Comment authorization handlers.</summary>
public sealed class CommentService(
    ICommentRepository commentRepository,
    ITaskRepository taskRepository,
    IAuthorizationService authorizationService,
    TimeProvider timeProvider,
    ILogger<CommentService> logger) : ICommentService
{
    private const string ResourceName = "Comment";

    public async Task<PagedResult<CommentResponse>> GetCommentsAsync(
        int taskId, CommentQueryParameters query, ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        await GetReadableTaskAsync(taskId, currentUser, TaskOperations.Read, cancellationToken);

        var (items, totalCount) = await commentRepository.GetPagedForTaskAsync(taskId, query.Page, query.PageSize, cancellationToken);
        return new PagedResult<CommentResponse>(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<CommentResponse> AddAsync(
        int taskId, CreateCommentRequest request, ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        await GetReadableTaskAsync(taskId, currentUser, TaskOperations.AddComment, cancellationToken);

        var comment = new Comment
        {
            TaskId = taskId,
            AuthorId = currentUser.GetUserId(),
            Content = request.Content.Trim(),
            CreatedAtUtc = UtcNow()
        };

        commentRepository.Add(comment);
        await commentRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Comment {CommentId} added to task {TaskId} by user {UserId}", comment.Id, taskId, comment.AuthorId);
        return await GetResponseAsync(comment.Id, cancellationToken);
    }

    public async Task<CommentResponse> UpdateAsync(
        int id, UpdateCommentRequest request, ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        var comment = await GetAuthorizedCommentAsync(id, currentUser, CommentOperations.Update, cancellationToken);

        comment.Content = request.Content.Trim();
        comment.UpdatedAtUtc = UtcNow(); // non-null UpdatedAtUtc = "edited" marker for clients
        await commentRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Comment {CommentId} edited by user {UserId}", id, currentUser.GetUserId());
        return await GetResponseAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(int id, ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        var comment = await GetAuthorizedCommentAsync(id, currentUser, CommentOperations.Delete, cancellationToken);

        commentRepository.Remove(comment);
        await commentRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Comment {CommentId} deleted by user {UserId}", id, currentUser.GetUserId());
    }

    /// <summary>The parent task must be visible (404) and allow the operation (403).</summary>
    private async Task GetReadableTaskAsync(
        int taskId, ClaimsPrincipal currentUser, OperationAuthorizationRequirement operation, CancellationToken cancellationToken)
    {
        var notFound = NotFoundException.For("Task", taskId);
        var task = await taskRepository.GetByIdWithAccessDataAsync(taskId, cancellationToken) ?? throw notFound;

        await authorizationService.AuthorizeOrNotFoundAsync(currentUser, task, TaskOperations.Read, notFound);
        if (operation != TaskOperations.Read)
        {
            await authorizationService.AuthorizeOrThrowAsync(currentUser, task, operation);
        }
    }

    /// <summary>Comment's task must be visible (else 404); then the comment operation is checked (else 403).</summary>
    private async Task<Comment> GetAuthorizedCommentAsync(
        int id, ClaimsPrincipal currentUser, OperationAuthorizationRequirement operation, CancellationToken cancellationToken)
    {
        var notFound = NotFoundException.For(ResourceName, id);
        var comment = await commentRepository.GetByIdWithAccessDataAsync(id, cancellationToken) ?? throw notFound;

        await authorizationService.AuthorizeOrNotFoundAsync(currentUser, comment.Task, TaskOperations.Read, notFound);
        await authorizationService.AuthorizeOrThrowAsync(currentUser, comment, operation);

        return comment;
    }

    private async Task<CommentResponse> GetResponseAsync(int id, CancellationToken cancellationToken) =>
        await commentRepository.GetResponseByIdAsync(id, cancellationToken)
        ?? throw NotFoundException.For(ResourceName, id);

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
