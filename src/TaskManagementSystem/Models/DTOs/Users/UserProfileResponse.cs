namespace TaskManagementSystem.Models.DTOs.Users;

/// <summary>A user's profile. Never contains the password hash, security stamp or other Identity internals.</summary>
/// <param name="Id">User id.</param>
/// <param name="Email">Email address.</param>
/// <param name="FirstName">First name.</param>
/// <param name="LastName">Last name.</param>
/// <param name="JobTitle">Optional job title.</param>
/// <param name="EmailConfirmed">Whether the email address has been verified.</param>
/// <param name="IsActive">false = deactivated by an Admin (cannot log in).</param>
/// <param name="Roles">Global roles: Admin, Manager and/or User.</param>
/// <param name="CreatedAtUtc">When the account was created (UTC).</param>
/// <param name="LastLoginAtUtc">Last successful login (UTC), or null.</param>
public sealed record UserProfileResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? JobTitle,
    bool EmailConfirmed,
    bool IsActive,
    IReadOnlyList<string> Roles,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc);
