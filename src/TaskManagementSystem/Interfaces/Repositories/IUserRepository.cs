using TaskManagementSystem.Models.DTOs.Users;

namespace TaskManagementSystem.Interfaces.Repositories;

/// <summary>
/// Read queries over users that UserManager can't do efficiently (filtering + paging + roles in ONE query).
/// All writes to users still go through UserManager (hashing, normalization, security stamps).
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// A filtered page of users, projected directly to DTOs (a "read model" query — no entities are
    /// loaded or tracked, and each user's roles come from a sub-query in the same SQL statement).
    /// </summary>
    Task<(IReadOnlyList<UserProfileResponse> Items, int TotalCount)> GetPagedAsync(
        UserQueryParameters query, CancellationToken cancellationToken);

    /// <summary>
    /// Up to <paramref name="limit"/> ACTIVE users whose email, first or last name contains
    /// <paramref name="search"/>, ordered by name. Inactive users are excluded because they can't be added to projects.
    /// </summary>
    Task<IReadOnlyList<UserLookupResponse>> SearchActiveAsync(string search, int limit, CancellationToken cancellationToken);
}
