using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using TaskManagementSystem.Constants;
using TaskManagementSystem.Exceptions;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Models.DTOs.Projects;
using TaskManagementSystem.Models.Entities;
using TaskManagementSystem.Services;
using TaskManagementSystem.UnitTests.TestHelpers;

namespace TaskManagementSystem.UnitTests.Services;

/// <summary>ProjectService: the 404-vs-403 rule and membership business rules.</summary>
public sealed class ProjectServiceTests
{
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<UserManager<ApplicationUser>> _userManager = MockIdentity.UserManager();
    private readonly ApplicationUser _owner = TestData.User("Owner");
    private readonly ApplicationUser _member = TestData.User("Member");
    private readonly ApplicationUser _outsider = TestData.User("Outsider");
    private readonly Project _project;

    public ProjectServiceTests()
    {
        _project = TestData.Project(_owner, _member);
        _projects.Setup(r => r.GetByIdWithMembersAsync(_project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_project);
    }

    // ApplicationDbContext is only used for the remove-member transaction; these tests never reach it.
    private ProjectService CreateService() => new(
        _projects.Object, _userManager.Object, null!, TestAuthorization.Create(),
        new FakeTimeProvider(TestData.Now), NullLogger<ProjectService>.Instance);

    [Fact]
    public async Task GetByIdAsync_Outsider_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateService().GetByIdAsync(_project.Id, TestUsers.Create(_outsider.Id, Roles.User), CancellationToken.None));
    }

    [Fact]
    public async Task GetByIdAsync_Member_ReturnsProjectWithOwnerFirst()
    {
        var response = await CreateService().GetByIdAsync(_project.Id, TestUsers.Create(_member.Id, Roles.User), CancellationToken.None);

        Assert.Equal("Apollo", response.Name);
        Assert.Equal(2, response.Members.Count);
        Assert.Equal("Owner", response.Members[0].MemberRole);
        Assert.Equal(_owner.Id, response.Members[0].UserId);
    }

    [Fact]
    public async Task UpdateAsync_Member_ThrowsForbidden_NotNotFound()
    {
        // The member CAN see the project, so an honest 403 is returned.
        await Assert.ThrowsAsync<ForbiddenException>(() => CreateService().UpdateAsync(
            _project.Id, new UpdateProjectRequest("Hacked", null, null, null),
            TestUsers.Create(_member.Id, Roles.User), CancellationToken.None));

        Assert.Equal("Apollo", _project.Name);
    }

    [Fact]
    public async Task AddMemberAsync_AlreadyMember_ThrowsConflict()
    {
        await Assert.ThrowsAsync<ConflictException>(() => CreateService().AddMemberAsync(
            _project.Id, new AddProjectMemberRequest(_member.Id),
            TestUsers.Create(_owner.Id, Roles.Manager), CancellationToken.None));
    }

    [Fact]
    public async Task AddMemberAsync_InactiveUser_ThrowsBusinessRule()
    {
        // Arrange
        var inactive = TestData.User("Inactive", isActive: false);
        _userManager.Setup(m => m.FindByIdAsync(inactive.Id.ToString())).ReturnsAsync(inactive);

        // Act + Assert
        await Assert.ThrowsAsync<BusinessRuleException>(() => CreateService().AddMemberAsync(
            _project.Id, new AddProjectMemberRequest(inactive.Id),
            TestUsers.Create(_owner.Id, Roles.Manager), CancellationToken.None));
        Assert.Equal(2, _project.Members.Count);
    }

    [Fact]
    public async Task RemoveMemberAsync_Owner_ThrowsBusinessRule()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => CreateService().RemoveMemberAsync(
            _project.Id, _owner.Id, TestUsers.Admin(), CancellationToken.None));
    }
}
