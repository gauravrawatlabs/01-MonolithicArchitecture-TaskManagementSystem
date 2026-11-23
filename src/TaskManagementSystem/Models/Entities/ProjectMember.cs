using TaskManagementSystem.Enums;

namespace TaskManagementSystem.Models.Entities;

/// <summary>
/// Explicit join entity for the many-to-many relationship Users ↔ Projects.
/// It is explicit (not a hidden join table) because it carries extra data: MemberRole and JoinedAtUtc.
/// Primary key: (ProjectId, UserId).
/// </summary>
public class ProjectMember
{
    public int ProjectId { get; set; }
    public Guid UserId { get; set; }
    public ProjectMemberRole MemberRole { get; set; } = ProjectMemberRole.Member;
    public DateTime JoinedAtUtc { get; set; }

    public Project Project { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
}
