using TaskManagementSystem.Models.DTOs.Users;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Mappings;

/// <summary>Manual ApplicationUser → DTO mapping. Only safe, public fields are copied.</summary>
public static class UserMappings
{
    public static UserProfileResponse ToProfileResponse(this ApplicationUser user, IEnumerable<string> roles) => new(
        user.Id,
        user.Email ?? string.Empty,
        user.FirstName,
        user.LastName,
        user.JobTitle,
        user.EmailConfirmed,
        roles.Order().ToList(),
        user.CreatedAtUtc,
        user.LastLoginAtUtc);
}
