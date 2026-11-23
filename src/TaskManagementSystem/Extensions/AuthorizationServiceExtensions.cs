using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using TaskManagementSystem.Exceptions;

namespace TaskManagementSystem.Extensions;

public static class AuthorizationServiceExtensions
{
    /// <summary>
    /// Runs resource-based authorization and throws ForbiddenException (→ 403) when it fails,
    /// so services can write a single line: await authorizationService.AuthorizeOrThrowAsync(...).
    /// </summary>
    public static async Task AuthorizeOrThrowAsync(
        this IAuthorizationService authorizationService,
        ClaimsPrincipal user,
        object resource,
        IAuthorizationRequirement requirement)
    {
        var result = await authorizationService.AuthorizeAsync(user, resource, requirement);
        if (!result.Succeeded)
        {
            throw new ForbiddenException();
        }
    }
}
