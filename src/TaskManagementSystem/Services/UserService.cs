using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using TaskManagementSystem.Authorization.Operations;
using TaskManagementSystem.Constants;
using TaskManagementSystem.Data;
using TaskManagementSystem.Exceptions;
using TaskManagementSystem.Extensions;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Mappings;
using TaskManagementSystem.Models.DTOs.Common;
using TaskManagementSystem.Models.DTOs.Users;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Services;

/// <summary>
/// User profiles and admin user management.
/// Coarse checks ("is Admin?") happen BEFORE the action via policies on the controller.
/// Fine-grained checks ("is this YOUR account?") happen HERE, after loading the resource,
/// via IAuthorizationService + UserAuthorizationHandler (resource-based authorization).
/// </summary>
public sealed class UserService(
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext dbContext,
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IAuthorizationService authorizationService,
    TimeProvider timeProvider,
    ILogger<UserService> logger) : IUserService
{
    private const string ResourceName = "User";

    public async Task<PagedResult<UserProfileResponse>> GetUsersAsync(
        UserQueryParameters query, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await userRepository.GetPagedAsync(query, cancellationToken);
        return new PagedResult<UserProfileResponse>(items, query.Page, query.PageSize, totalCount);
    }

    public Task<IReadOnlyList<UserLookupResponse>> LookupAsync(
        UserLookupQueryParameters query, CancellationToken cancellationToken) =>
        // The validator guarantees Search is present (≥ 2 chars) and Limit is 1–20.
        userRepository.SearchActiveAsync(query.Search!, query.Limit, cancellationToken);

    public async Task<UserProfileResponse> GetByIdAsync(
        Guid id, ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(id);

        // Resource-based check: needs the loaded user to decide "self or Admin".
        await authorizationService.AuthorizeOrThrowAsync(currentUser, user, UserOperations.Read);

        return user.ToProfileResponse(await userManager.GetRolesAsync(user));
    }

    public async Task<UserProfileResponse> UpdateProfileAsync(
        Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedException("The user account no longer exists or is inactive.");
        }

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.JobTitle = string.IsNullOrWhiteSpace(request.JobTitle) ? null : request.JobTitle.Trim();
        user.UpdatedAtUtc = UtcNow();

        EnsureSucceeded(await userManager.UpdateAsync(user));

        logger.LogInformation("User {UserId} updated their profile", user.Id);
        return user.ToProfileResponse(await userManager.GetRolesAsync(user));
    }

    public async Task<UserProfileResponse> UpdateRolesAsync(
        Guid id, UpdateUserRolesRequest request, Guid currentUserId, CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(id);

        // Normalize casing to the canonical role names ("manager" → "Manager").
        var requestedRoles = request.Roles
            .Select(role => Roles.All.First(known => known.Equals(role, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .ToList();

        // Business rule: an Admin must not lock themselves out of administration.
        // (It also guarantees at least one Admin always remains: the caller.)
        if (user.Id == currentUserId && !requestedRoles.Contains(Roles.Admin))
        {
            throw new BusinessRuleException("You cannot remove the Admin role from your own account.");
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        var rolesToRemove = currentRoles.Except(requestedRoles).ToList();
        var rolesToAdd = requestedRoles.Except(currentRoles).ToList();

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            if (rolesToRemove.Count > 0)
            {
                EnsureSucceeded(await userManager.RemoveFromRolesAsync(user, rolesToRemove));
            }

            if (rolesToAdd.Count > 0)
            {
                EnsureSucceeded(await userManager.AddToRolesAsync(user, rolesToAdd));
            }

            await transaction.CommitAsync(cancellationToken);
        }

        // The user's CURRENT access token still carries the old roles until it expires (≤ 15 min);
        // the next refresh reads roles from the database and issues a token with the new roles.
        logger.LogInformation(
            "Admin {AdminId} changed roles of user {UserId}: added [{Added}], removed [{Removed}]",
            currentUserId, user.Id, string.Join(", ", rolesToAdd), string.Join(", ", rolesToRemove));

        return user.ToProfileResponse(requestedRoles);
    }

    public async Task<UserProfileResponse> UpdateStatusAsync(
        Guid id, UpdateUserStatusRequest request, Guid currentUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(id);

        if (user.Id == currentUserId && !request.IsActive)
        {
            throw new BusinessRuleException("You cannot deactivate your own account.");
        }

        if (user.IsActive != request.IsActive)
        {
            user.IsActive = request.IsActive;
            user.UpdatedAtUtc = UtcNow();
            EnsureSucceeded(await userManager.UpdateAsync(user));

            if (!request.IsActive)
            {
                // New security stamp → any outstanding confirmation / password-reset links stop working.
                EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user));
                var revoked = await refreshTokenRepository.RevokeAllActiveForUserAsync(
                    user.Id, UtcNow(), ipAddress, "Account deactivated", cancellationToken);
                logger.LogInformation("{Count} sessions of user {UserId} revoked on deactivation", revoked, user.Id);
            }

            logger.LogInformation("Admin {AdminId} set user {UserId} active = {IsActive}",
                currentUserId, user.Id, request.IsActive);
        }

        return user.ToProfileResponse(await userManager.GetRolesAsync(user));
    }

    private async Task<ApplicationUser> FindUserAsync(Guid id) =>
        await userManager.FindByIdAsync(id.ToString()) ?? throw NotFoundException.For(ResourceName, id);

    /// <summary>Identity reports failures as results, not exceptions — translate them.</summary>
    private static void EnsureSucceeded(IdentityResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure)))
        {
            throw new ConflictException("The user was modified by someone else. Reload and try again.");
        }

        throw new InvalidOperationException(
            "Identity operation failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
