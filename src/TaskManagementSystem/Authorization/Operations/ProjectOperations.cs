using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace TaskManagementSystem.Authorization.Operations;

/// <summary>Operations that can be performed on a project (resource-based authorization).</summary>
public static class ProjectOperations
{
    public static readonly OperationAuthorizationRequirement Read = new() { Name = nameof(Read) };
    public static readonly OperationAuthorizationRequirement Update = new() { Name = nameof(Update) };
    public static readonly OperationAuthorizationRequirement Delete = new() { Name = nameof(Delete) };
    public static readonly OperationAuthorizationRequirement ManageMembers = new() { Name = nameof(ManageMembers) };
}
