using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using TaskManagementSystem.Constants;
using TaskManagementSystem.Enums;
using TaskManagementSystem.Exceptions;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Models.DTOs.Tasks;
using TaskManagementSystem.Models.Entities;
using TaskManagementSystem.Services;
using TaskManagementSystem.UnitTests.TestHelpers;

namespace TaskManagementSystem.UnitTests.Services;

/// <summary>
/// TaskService: mocked repositories + UserManager, but the REAL authorization handlers
/// (TestAuthorization), so the tests cover "who may do what" exactly as production does.
/// </summary>
public sealed class TaskServiceTests
{
    private readonly Mock<ITaskRepository> _tasks = new();
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly Mock<UserManager<ApplicationUser>> _userManager = MockIdentity.UserManager();
    private readonly FakeTimeProvider _clock = new(TestData.Now);

    private readonly ApplicationUser _owner = TestData.User("Owner");
    private readonly ApplicationUser _creator = TestData.User("Creator");
    private readonly ApplicationUser _assignee = TestData.User("Assignee");
    private readonly ApplicationUser _member = TestData.User("Member");
    private readonly ApplicationUser _outsider = TestData.User("Outsider");
    private readonly Project _project;

    public TaskServiceTests()
    {
        _project = TestData.Project(_owner, _creator, _assignee, _member);
        _projects.Setup(r => r.GetByIdWithMembersAsync(_project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_project);
        _tasks.Setup(r => r.GetResponseByIdAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((int id, DateTime _, CancellationToken _) => TestData.TaskResponse(id));
    }

    private TaskService CreateService() => new(
        _tasks.Object, _projects.Object, _categories.Object, _userManager.Object,
        TestAuthorization.Create(), _clock, NullLogger<TaskService>.Instance);

    private TaskItem GivenTask(TaskItemStatus status = TaskItemStatus.Todo)
    {
        var task = TestData.NewTask(_project, _creator.Id, _assignee.Id, status);
        _tasks.Setup(r => r.GetByIdWithAccessDataAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        return task;
    }

    private static ClaimsPrincipal As(ApplicationUser user, string role = Roles.User) => TestUsers.Create(user.Id, role);

    // ---------- Status workflow ----------

    [Theory]
    [InlineData(TaskItemStatus.Todo, TaskItemStatus.Done)]
    [InlineData(TaskItemStatus.Todo, TaskItemStatus.InReview)]
    [InlineData(TaskItemStatus.Done, TaskItemStatus.Todo)]
    public async Task ChangeStatusAsync_InvalidTransition_ThrowsConflict_AndDoesNotSave(TaskItemStatus from, TaskItemStatus to)
    {
        // Arrange
        var task = GivenTask(from);
        var service = CreateService();

        // Act
        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ChangeStatusAsync(task.Id, new ChangeTaskStatusRequest(to), As(_owner, Roles.Manager), CancellationToken.None));

        // Assert
        Assert.Contains("Allowed next statuses", exception.Message);
        Assert.Equal(from, task.Status);
        _tasks.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangeStatusAsync_AssigneeMovesToDone_SetsCompletedAtFromClock()
    {
        // Arrange
        var task = GivenTask(TaskItemStatus.InReview);
        var service = CreateService();

        // Act
        await service.ChangeStatusAsync(task.Id, new ChangeTaskStatusRequest(TaskItemStatus.Done), As(_assignee), CancellationToken.None);

        // Assert
        Assert.Equal(TaskItemStatus.Done, task.Status);
        Assert.Equal(TestData.Now.UtcDateTime, task.CompletedAtUtc);
        _tasks.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangeStatusAsync_AssigneeReopensDoneTask_ThrowsForbidden()
    {
        var task = GivenTask(TaskItemStatus.Done);

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateService().ChangeStatusAsync(
            task.Id, new ChangeTaskStatusRequest(TaskItemStatus.InProgress), As(_assignee), CancellationToken.None));

        Assert.Equal(TaskItemStatus.Done, task.Status);
    }

    [Fact]
    public async Task ChangeStatusAsync_OwnerReopensDoneTask_ClearsCompletedAt()
    {
        // Arrange
        var task = GivenTask(TaskItemStatus.Done);
        task.CompletedAtUtc = TestData.Now.UtcDateTime.AddDays(-1);

        // Act
        await CreateService().ChangeStatusAsync(
            task.Id, new ChangeTaskStatusRequest(TaskItemStatus.InProgress), As(_owner, Roles.Manager), CancellationToken.None);

        // Assert
        Assert.Equal(TaskItemStatus.InProgress, task.Status);
        Assert.Null(task.CompletedAtUtc);
    }

    [Fact]
    public async Task ChangeStatusAsync_OtherMember_ThrowsForbidden()
    {
        var task = GivenTask();

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateService().ChangeStatusAsync(
            task.Id, new ChangeTaskStatusRequest(TaskItemStatus.InProgress), As(_member), CancellationToken.None));
    }

    [Fact]
    public async Task ChangeStatusAsync_Outsider_ThrowsNotFound_NotForbidden()
    {
        var task = GivenTask();

        // 404, not 403: outsiders must not learn that task 10 exists.
        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().ChangeStatusAsync(
            task.Id, new ChangeTaskStatusRequest(TaskItemStatus.InProgress), As(_outsider), CancellationToken.None));
    }

    // ---------- Assignment ----------

    [Fact]
    public async Task AssignAsync_ToNonMember_ThrowsBusinessRule()
    {
        var task = GivenTask();

        await Assert.ThrowsAsync<BusinessRuleException>(() => CreateService().AssignAsync(
            task.Id, new AssignTaskRequest(_outsider.Id), As(_owner, Roles.Manager), CancellationToken.None));

        Assert.Equal(_assignee.Id, task.AssignedToId);
    }

    [Fact]
    public async Task AssignAsync_ToInactiveMember_ThrowsBusinessRule()
    {
        // Arrange
        var task = GivenTask();
        _member.IsActive = false;
        _userManager.Setup(m => m.FindByIdAsync(_member.Id.ToString())).ReturnsAsync(_member);

        // Act + Assert
        await Assert.ThrowsAsync<BusinessRuleException>(() => CreateService().AssignAsync(
            task.Id, new AssignTaskRequest(_member.Id), As(_owner, Roles.Manager), CancellationToken.None));
    }

    [Fact]
    public async Task AssignAsync_ToActiveMember_SetsAssignee()
    {
        // Arrange
        var task = GivenTask();
        _userManager.Setup(m => m.FindByIdAsync(_member.Id.ToString())).ReturnsAsync(_member);

        // Act
        await CreateService().AssignAsync(task.Id, new AssignTaskRequest(_member.Id), As(_owner, Roles.Manager), CancellationToken.None);

        // Assert
        Assert.Equal(_member.Id, task.AssignedToId);
        Assert.Equal(TestData.Now.UtcDateTime, task.UpdatedAtUtc);
    }

    // ---------- Create / update business rules ----------

    [Fact]
    public async Task CreateAsync_Valid_AppliesDefaultsAndAuditFields()
    {
        // Arrange
        TaskItem? added = null;
        _tasks.Setup(r => r.Add(It.IsAny<TaskItem>())).Callback<TaskItem>(t => added = t);
        var request = new CreateTaskRequest(_project.Id, "  Write docs  ", null, null, null, null);

        // Act
        await CreateService().CreateAsync(request, As(_member), CancellationToken.None);

        // Assert
        Assert.NotNull(added);
        Assert.Equal("Write docs", added.Title);
        Assert.Equal(TaskItemStatus.Todo, added.Status);
        Assert.Equal(TaskPriority.Medium, added.Priority);
        Assert.Equal(_member.Id, added.CreatedById);
        Assert.Null(added.AssignedToId);
        Assert.Equal(TestData.Now.UtcDateTime, added.CreatedAtUtc);
    }

    [Fact]
    public async Task CreateAsync_DueDateAfterProjectEnd_ThrowsBusinessRule()
    {
        _project.EndDate = new DateOnly(2026, 12, 31);
        var request = new CreateTaskRequest(_project.Id, "Late task", null, null, new DateTime(2027, 1, 5, 0, 0, 0, DateTimeKind.Utc), null);

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateService().CreateAsync(request, As(_member), CancellationToken.None));

        Assert.Contains("2026-12-31", exception.Message);
        _tasks.Verify(r => r.Add(It.IsAny<TaskItem>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_UnknownCategory_ThrowsBusinessRule()
    {
        _categories.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((Category?)null);

        await Assert.ThrowsAsync<BusinessRuleException>(() => CreateService().CreateAsync(
            new CreateTaskRequest(_project.Id, "Title", null, null, null, 999), As(_member), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_ByOutsider_ThrowsNotFoundForTheProject()
    {
        var exception = await Assert.ThrowsAsync<NotFoundException>(() => CreateService().CreateAsync(
            new CreateTaskRequest(_project.Id, "Title", null, null, null, null), As(_outsider), CancellationToken.None));

        Assert.Contains("Project", exception.Message);
    }

    [Fact]
    public async Task UpdateAsync_ChangingDueDateToThePast_ThrowsValidationError()
    {
        var task = GivenTask();
        var request = new UpdateTaskRequest("Title", null, TaskPriority.Low, TestData.Now.UtcDateTime.AddDays(-1), null);

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            CreateService().UpdateAsync(task.Id, request, As(_creator), CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey("dueDateUtc"));
    }

    [Fact]
    public async Task UpdateAsync_OverdueTaskWithUnchangedDueDate_IsStillEditable()
    {
        // Arrange: the task is already overdue.
        var task = GivenTask();
        task.DueDateUtc = TestData.Now.UtcDateTime.AddDays(-2);
        var request = new UpdateTaskRequest("Fixed typo", null, TaskPriority.High, task.DueDateUtc, null);

        // Act
        await CreateService().UpdateAsync(task.Id, request, As(_creator), CancellationToken.None);

        // Assert
        Assert.Equal("Fixed typo", task.Title);
        Assert.Equal(TaskPriority.High, task.Priority);
    }
}
