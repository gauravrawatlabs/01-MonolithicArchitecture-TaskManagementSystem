using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace TaskManagementSystem.Authorization.Operations;

/// <summary>Operations on a task (resource-based authorization).</summary>
public static class TaskOperations
{
    public static readonly OperationAuthorizationRequirement Read = new() { Name = nameof(Read) };
    public static readonly OperationAuthorizationRequirement Update = new() { Name = nameof(Update) };
    public static readonly OperationAuthorizationRequirement Delete = new() { Name = nameof(Delete) };
    public static readonly OperationAuthorizationRequirement Assign = new() { Name = nameof(Assign) };
    public static readonly OperationAuthorizationRequirement ChangeStatus = new() { Name = nameof(ChangeStatus) };

    /// <summary>Reopen a Done task / restore a Cancelled task.</summary>
    public static readonly OperationAuthorizationRequirement Reopen = new() { Name = nameof(Reopen) };

    public static readonly OperationAuthorizationRequirement AddComment = new() { Name = nameof(AddComment) };
}

/// <summary>Operations on a comment (resource-based authorization).</summary>
public static class CommentOperations
{
    public static readonly OperationAuthorizationRequirement Update = new() { Name = nameof(Update) };
    public static readonly OperationAuthorizationRequirement Delete = new() { Name = nameof(Delete) };
}
