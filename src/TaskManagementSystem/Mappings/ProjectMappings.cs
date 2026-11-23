using TaskManagementSystem.Enums;
using TaskManagementSystem.Models.DTOs.Projects;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Mappings;

/// <summary>Project → DTO mapping. Requires Owner and Members.User to be loaded.</summary>
public static class ProjectMappings
{
    public static ProjectDetailsResponse ToDetailsResponse(this Project project) => new(
        project.Id,
        project.Name,
        project.Description,
        project.StartDate,
        project.EndDate,
        project.OwnerId,
        FullName(project.Owner),
        project.Members
            .OrderBy(m => m.MemberRole == ProjectMemberRole.Owner ? 0 : 1)
            .ThenBy(m => m.User.FirstName)
            .ThenBy(m => m.User.LastName)
            .Select(m => m.ToResponse())
            .ToList(),
        project.CreatedAtUtc,
        project.UpdatedAtUtc);

    public static ProjectMemberResponse ToResponse(this ProjectMember member) => new(
        member.UserId,
        FullName(member.User),
        member.User.Email ?? string.Empty,
        member.MemberRole.ToString(),
        member.JoinedAtUtc);

    private static string FullName(ApplicationUser user) => $"{user.FirstName} {user.LastName}".Trim();
}
