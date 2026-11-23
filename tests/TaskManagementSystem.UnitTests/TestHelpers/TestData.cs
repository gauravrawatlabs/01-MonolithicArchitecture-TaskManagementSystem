using TaskManagementSystem.Enums;
using TaskManagementSystem.Models.DTOs.Tasks;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.UnitTests.TestHelpers;

/// <summary>Small builders for in-memory entity graphs (no database involved).</summary>
public static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);

    public static ApplicationUser User(string firstName, bool isActive = true)
    {
        var email = $"{firstName.ToLowerInvariant()}@example.com";
        return new ApplicationUser
        {
            Id = Guid.NewGuid(),
            FirstName = firstName,
            LastName = "Tester",
            Email = email,
            UserName = email,
            IsActive = isActive
        };
    }

    /// <summary>A project owned by <paramref name="owner"/>, with the owner and <paramref name="members"/> as members.</summary>
    public static Project Project(ApplicationUser owner, params ApplicationUser[] members)
    {
        var project = new Project
        {
            Id = 1,
            Name = "Apollo",
            OwnerId = owner.Id,
            Owner = owner,
            CreatedAtUtc = Now.UtcDateTime
        };

        project.Members.Add(new ProjectMember
        {
            ProjectId = project.Id, UserId = owner.Id, User = owner, MemberRole = ProjectMemberRole.Owner
        });
        foreach (var member in members)
        {
            project.Members.Add(new ProjectMember
            {
                ProjectId = project.Id, UserId = member.Id, User = member, MemberRole = ProjectMemberRole.Member
            });
        }

        return project;
    }

    public static TaskItem NewTask(
        Project project, Guid createdById, Guid? assignedToId = null, TaskItemStatus status = TaskItemStatus.Todo) => new()
    {
        Id = 10,
        Title = "Build login page",
        Project = project,
        ProjectId = project.Id,
        CreatedById = createdById,
        AssignedToId = assignedToId,
        Status = status,
        Priority = TaskPriority.Medium,
        CreatedAtUtc = Now.UtcDateTime
    };

    /// <summary>A placeholder response; services return whatever the repository projects.</summary>
    public static TaskResponse TaskResponse(int id) => new(
        id, "Build login page", null, TaskItemStatus.Todo, TaskPriority.Medium, null, null, false,
        1, "Apollo", null, null, Guid.Empty, "Creator Tester", null, null, Now.UtcDateTime, null);
}
