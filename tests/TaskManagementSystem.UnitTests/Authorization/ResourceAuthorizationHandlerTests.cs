using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using TaskManagementSystem.Authorization.Handlers;
using TaskManagementSystem.Authorization.Operations;
using TaskManagementSystem.Constants;
using TaskManagementSystem.Models.Entities;
using TaskManagementSystem.UnitTests.TestHelpers;

namespace TaskManagementSystem.UnitTests.Authorization;

/// <summary>
/// The permission matrices from docs/09 and docs/10, tested cell by cell.
/// Handlers are pure (no database), so each test is: build a resource + a user, run the handler,
/// check HasSucceeded.
/// </summary>
public sealed class ResourceAuthorizationHandlerTests
{
    public enum Who { Admin, ProjectOwner, Creator, Assignee, OtherMember, Outsider }

    private readonly ApplicationUser _owner = TestData.User("Owner");
    private readonly ApplicationUser _creator = TestData.User("Creator");
    private readonly ApplicationUser _assignee = TestData.User("Assignee");
    private readonly ApplicationUser _member = TestData.User("Member");
    private readonly ApplicationUser _outsider = TestData.User("Outsider");

    private ClaimsPrincipal Principal(Who who) => who switch
    {
        Who.Admin => TestUsers.Admin(),
        Who.ProjectOwner => TestUsers.Create(_owner.Id, Roles.Manager),
        Who.Creator => TestUsers.Create(_creator.Id, Roles.User),
        Who.Assignee => TestUsers.Create(_assignee.Id, Roles.User),
        Who.OtherMember => TestUsers.Create(_member.Id, Roles.User),
        _ => TestUsers.Create(_outsider.Id, Roles.User)
    };

    private Project NewProject() => TestData.Project(_owner, _creator, _assignee, _member);

    private static async Task<bool> IsAllowedAsync<TResource>(
        AuthorizationHandler<OperationAuthorizationRequirement, TResource> handler,
        ClaimsPrincipal user, TResource resource, OperationAuthorizationRequirement operation)
    {
        var context = new AuthorizationHandlerContext([operation], user, resource);
        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    // ---------- Projects ----------

    [Theory]
    [InlineData(Who.Admin, "Read", true)]
    [InlineData(Who.Admin, "Delete", true)]
    [InlineData(Who.ProjectOwner, "Read", true)]
    [InlineData(Who.ProjectOwner, "Update", true)]
    [InlineData(Who.ProjectOwner, "Delete", true)]
    [InlineData(Who.ProjectOwner, "ManageMembers", true)]
    [InlineData(Who.OtherMember, "Read", true)]
    [InlineData(Who.OtherMember, "Update", false)]
    [InlineData(Who.OtherMember, "Delete", false)]
    [InlineData(Who.OtherMember, "ManageMembers", false)]
    [InlineData(Who.Outsider, "Read", false)]
    [InlineData(Who.Outsider, "Update", false)]
    public async Task ProjectHandler_FollowsPermissionMatrix(Who who, string operation, bool expected)
    {
        // Arrange
        var requirement = operation switch
        {
            "Read" => ProjectOperations.Read,
            "Update" => ProjectOperations.Update,
            "Delete" => ProjectOperations.Delete,
            _ => ProjectOperations.ManageMembers
        };

        // Act
        var allowed = await IsAllowedAsync(new ProjectAuthorizationHandler(), Principal(who), NewProject(), requirement);

        // Assert
        Assert.Equal(expected, allowed);
    }

    [Fact]
    public async Task ProjectHandler_WhenMembersAreNotLoaded_DeniesMembers_FailsClosed()
    {
        // Arrange: same project, but the Members collection was not loaded (empty).
        var project = new Project { Id = 1, Name = "Apollo", OwnerId = _owner.Id };

        // Act
        var allowed = await IsAllowedAsync(new ProjectAuthorizationHandler(), Principal(Who.OtherMember), project, ProjectOperations.Read);

        // Assert: denied — never accidentally allowed.
        Assert.False(allowed);
    }

    // ---------- Tasks ----------

    public static TheoryData<Who, string, bool> TaskMatrix() => new()
    {
        { Who.Admin, "Reopen", true },
        { Who.ProjectOwner, "Update", true }, { Who.ProjectOwner, "Assign", true }, { Who.ProjectOwner, "Reopen", true },
        { Who.Creator, "Update", true }, { Who.Creator, "Delete", true }, { Who.Creator, "ChangeStatus", true },
        { Who.Creator, "Assign", false }, { Who.Creator, "Reopen", false },
        { Who.Assignee, "ChangeStatus", true }, { Who.Assignee, "Update", false }, { Who.Assignee, "Delete", false },
        { Who.OtherMember, "Read", true }, { Who.OtherMember, "AddComment", true },
        { Who.OtherMember, "ChangeStatus", false }, { Who.OtherMember, "Update", false },
        { Who.Outsider, "Read", false }, { Who.Outsider, "AddComment", false }
    };

    [Theory]
    [MemberData(nameof(TaskMatrix))]
    public async Task TaskHandler_FollowsPermissionMatrix(Who who, string operation, bool expected)
    {
        // Arrange
        var task = TestData.NewTask(NewProject(), _creator.Id, _assignee.Id);
        var requirement = new OperationAuthorizationRequirement { Name = operation };

        // Act
        var allowed = await IsAllowedAsync(new TaskAuthorizationHandler(), Principal(who), task, requirement);

        // Assert
        Assert.Equal(expected, allowed);
    }

    [Fact]
    public async Task TaskHandler_CreatorRemovedFromProject_LosesAllRights()
    {
        // Arrange: the creator is no longer a project member.
        var project = TestData.Project(_owner, _assignee);
        var task = TestData.NewTask(project, _creator.Id, _assignee.Id);

        // Act
        var canUpdate = await IsAllowedAsync(new TaskAuthorizationHandler(), Principal(Who.Creator), task, TaskOperations.Update);
        var canRead = await IsAllowedAsync(new TaskAuthorizationHandler(), Principal(Who.Creator), task, TaskOperations.Read);

        // Assert
        Assert.False(canUpdate);
        Assert.False(canRead);
    }

    [Fact]
    public async Task TaskHandler_WhenProjectNotLoaded_Denies()
    {
        var task = new TaskItem { Id = 1, Title = "x", CreatedById = _creator.Id, Project = null! };

        var allowed = await IsAllowedAsync(new TaskAuthorizationHandler(), Principal(Who.Creator), task, TaskOperations.Read);

        Assert.False(allowed);
    }

    // ---------- Comments ----------

    [Theory]
    [InlineData(Who.Creator, "Update", true)]        // Creator = comment author in this test
    [InlineData(Who.Creator, "Delete", true)]
    [InlineData(Who.Admin, "Update", false)]         // nobody edits someone else's words
    [InlineData(Who.Admin, "Delete", true)]
    [InlineData(Who.ProjectOwner, "Update", false)]
    [InlineData(Who.ProjectOwner, "Delete", true)]   // moderation
    [InlineData(Who.OtherMember, "Update", false)]
    [InlineData(Who.OtherMember, "Delete", false)]
    public async Task CommentHandler_FollowsPermissionMatrix(Who who, string operation, bool expected)
    {
        // Arrange
        var task = TestData.NewTask(NewProject(), _creator.Id);
        var comment = new Comment { Id = 5, TaskId = task.Id, Task = task, AuthorId = _creator.Id, Content = "Hi" };
        var requirement = operation == "Update" ? CommentOperations.Update : CommentOperations.Delete;

        // Act
        var allowed = await IsAllowedAsync(new CommentAuthorizationHandler(), Principal(who), comment, requirement);

        // Assert
        Assert.Equal(expected, allowed);
    }
}
