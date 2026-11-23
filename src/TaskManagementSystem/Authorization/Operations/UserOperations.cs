using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace TaskManagementSystem.Authorization.Operations;

/// <summary>
/// Operations that can be performed on a user account (resource-based authorization).
/// Usage: authorizationService.AuthorizeAsync(User, targetUser, UserOperations.Read)
/// </summary>
public static class UserOperations
{
    public static readonly OperationAuthorizationRequirement Read = new() { Name = nameof(Read) };
}
