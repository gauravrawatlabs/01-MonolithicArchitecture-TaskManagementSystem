using TaskManagementSystem.Enums;
using TaskManagementSystem.Models.DTOs.Common;

namespace TaskManagementSystem.Models.DTOs.Users;

/// <summary>Filters, sorting and paging for the admin user list (bound from the query string).</summary>
public sealed class UserQueryParameters : PagedQueryParameters
{
    /// <summary>Matches email, first name or last name (contains).</summary>
    public string? Search { get; init; }

    /// <summary>Only users with this role: Admin, Manager or User.</summary>
    public string? Role { get; init; }

    /// <summary>true = active users only, false = deactivated users only, empty = both.</summary>
    public bool? IsActive { get; init; }

    /// <summary>Email (default), Name, CreatedAt or LastLogin.</summary>
    public UserSortField SortBy { get; init; } = UserSortField.Email;

    /// <summary>Asc or Desc. Default: Asc for Email/Name, Desc for CreatedAt/LastLogin (newest first).</summary>
    public SortDirection? SortDirection { get; init; }
}

/// <summary>
/// Search for the user lookup (GET /api/users/lookup), bound from the query string.
/// Deliberately NOT paged: it feeds an autocomplete box, so it returns only the best few matches.
/// </summary>
public sealed class UserLookupQueryParameters
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 20;
    public const int MinSearchLength = 2;

    /// <summary>Matches email, first name or last name (contains). At least 2 characters.</summary>
    public string? Search { get; init; }

    /// <summary>Maximum number of results, 1–20 (default 10).</summary>
    public int Limit { get; init; } = DefaultLimit;
}

/// <summary>Profile fields the user may change themselves.</summary>
/// <param name="FirstName">First name (max 50 characters).</param>
/// <param name="LastName">Last name (max 50 characters).</param>
/// <param name="JobTitle">Optional job title (max 100 characters).</param>
public sealed record UpdateProfileRequest(string FirstName, string LastName, string? JobTitle);

/// <summary>Replaces a user's roles (Admin only).</summary>
/// <param name="Roles">The complete new set of roles, e.g. ["Manager"] or ["Admin", "Manager"].</param>
public sealed record UpdateUserRolesRequest(IReadOnlyList<string> Roles);

/// <summary>Activates or deactivates an account (Admin only).</summary>
/// <param name="IsActive">false = deactivate (cannot log in, sessions revoked); true = reactivate.</param>
public sealed record UpdateUserStatusRequest(bool IsActive);
