namespace TaskManagementSystem.Models.DTOs.Users;

/// <summary>Filters and paging for the admin user list (bound from the query string).</summary>
public sealed class UserQueryParameters
{
    /// <summary>Matches email, first name or last name (contains).</summary>
    public string? Search { get; init; }

    /// <summary>Only users with this role: Admin, Manager or User.</summary>
    public string? Role { get; init; }

    /// <summary>true = active users only, false = deactivated users only, empty = both.</summary>
    public bool? IsActive { get; init; }

    /// <summary>1-based page number (default 1).</summary>
    public int Page { get; init; } = 1;

    /// <summary>Items per page, 1–100 (default 20).</summary>
    public int PageSize { get; init; } = 20;
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
