using System.Security.Claims;
using TaskManagementSystem.Models.DTOs.Common;
using TaskManagementSystem.Models.DTOs.Users;

namespace TaskManagementSystem.Interfaces.Services;

/// <summary>User profiles and admin user management.</summary>
public interface IUserService
{
    /// <summary>Admin: filtered, paged list of users.</summary>
    Task<PagedResult<UserProfileResponse>> GetUsersAsync(UserQueryParameters query, CancellationToken cancellationToken);

    /// <summary>A user's profile — only for that user or an Admin (resource-based authorization).</summary>
    /// <exception cref="Exceptions.NotFoundException">No such user.</exception>
    /// <exception cref="Exceptions.ForbiddenException">Not your account and you are not an Admin.</exception>
    Task<UserProfileResponse> GetByIdAsync(Guid id, ClaimsPrincipal currentUser, CancellationToken cancellationToken);

    /// <summary>Updates the current user's own profile.</summary>
    Task<UserProfileResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken);

    /// <summary>Admin: replaces a user's roles.</summary>
    /// <exception cref="Exceptions.BusinessRuleException">An Admin tried to remove their own Admin role.</exception>
    Task<UserProfileResponse> UpdateRolesAsync(
        Guid id, UpdateUserRolesRequest request, Guid currentUserId, CancellationToken cancellationToken);

    /// <summary>Admin: activates or deactivates an account. Deactivation revokes all sessions.</summary>
    /// <exception cref="Exceptions.BusinessRuleException">An Admin tried to deactivate their own account.</exception>
    Task<UserProfileResponse> UpdateStatusAsync(
        Guid id, UpdateUserStatusRequest request, Guid currentUserId, string? ipAddress, CancellationToken cancellationToken);
}
