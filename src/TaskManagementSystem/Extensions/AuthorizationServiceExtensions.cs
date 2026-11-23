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

    /// <summary>
    /// Like <see cref="AuthorizeOrThrowAsync"/>, but throws the given NotFoundException instead of 403.
    /// Use it for the READ check on resources with guessable ids (e.g. int project ids): callers who may
    /// not see a resource can't distinguish "doesn't exist" from "exists but not yours".
    /// </summary>
    public static async Task AuthorizeOrNotFoundAsync(
        this IAuthorizationService authorizationService,
        ClaimsPrincipal user,
        object resource,
        IAuthorizationRequirement requirement,
        NotFoundException notFound)
    {
        var result = await authorizationService.AuthorizeAsync(user, resource, requirement);
        if (!result.Succeeded)
        {
            throw notFound;
        }
    }
}
