using System.Net;
using System.Net.Http.Json;
using TaskManagementSystem.Enums;
using TaskManagementSystem.IntegrationTests.Infrastructure;
using TaskManagementSystem.Models.DTOs.Comments;
using TaskManagementSystem.Models.DTOs.Common;
using TaskManagementSystem.Models.DTOs.Dashboard;
using TaskManagementSystem.Models.DTOs.Projects;
using TaskManagementSystem.Models.DTOs.Tasks;

namespace TaskManagementSystem.IntegrationTests;

/// <summary>CRUD flows across modules: projects → members → tasks → workflow → comments → dashboard.</summary>
public sealed class ProjectAndTaskApiTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task ProjectAndTaskLifecycle_EnforcesRulesEndToEnd()
    {
        // ---------- Arrange: a manager's project with one member; one outsider ----------
        var manager = await CreateUserAsync(role: "Manager", prefix: "manager");
        var member = await CreateUserAsync(prefix: "member");
        var outsider = await CreateUserAsync(prefix: "outsider");
        var asManager = ClientFor(manager.AccessToken);
        var asMember = ClientFor(member.AccessToken);
        var asOutsider = ClientFor(outsider.AccessToken);

        var projectResponse = await asManager.PostAsJsonAsync("/api/projects", new { name = "Lifecycle", endDate = "2099-12-31" });
        var project = await ReadAsync<ProjectDetailsResponse>(projectResponse);
        var addMember = await asManager.PostAsJsonAsync($"/api/projects/{project.Id}/members", new { userId = member.Id });

        // ---------- Act + Assert: visibility ----------
        Assert.Equal(HttpStatusCode.Created, projectResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, addMember.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await asOutsider.GetAsync($"/api/projects/{project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await asMember.GetAsync($"/api/projects/{project.Id}")).StatusCode);

        // ---------- Tasks ----------
        var created = await asMember.PostAsJsonAsync("/api/tasks", new { projectId = project.Id, title = "Write tests", priority = "High" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var task = await ReadAsync<TaskResponse>(created);
        Assert.Equal(TaskItemStatus.Todo, task.Status);
        Assert.Equal(TaskPriority.High, task.Priority);

        var outsiderCreate = await asOutsider.PostAsJsonAsync("/api/tasks", new { projectId = project.Id, title = "X" });
        Assert.Equal(HttpStatusCode.NotFound, outsiderCreate.StatusCode);

        var memberAssigns = await asMember.PatchAsJsonAsync($"/api/tasks/{task.Id}/assignee", new { assignedToId = member.Id });
        Assert.Equal(HttpStatusCode.Forbidden, memberAssigns.StatusCode);   // only the project owner assigns

        var ownerAssigns = await asManager.PatchAsJsonAsync($"/api/tasks/{task.Id}/assignee", new { assignedToId = member.Id });
        Assert.Equal(member.Id, (await ReadAsync<TaskResponse>(ownerAssigns)).AssignedToId);

        var assignOutsider = await asManager.PatchAsJsonAsync($"/api/tasks/{task.Id}/assignee", new { assignedToId = outsider.Id });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, assignOutsider.StatusCode);

        // ---------- Workflow ----------
        var skip = await asMember.PatchAsJsonAsync($"/api/tasks/{task.Id}/status", new { status = "Done" });
        Assert.Equal(HttpStatusCode.Conflict, skip.StatusCode);
        foreach (var status in new[] { "InProgress", "InReview", "Done" })
        {
            var step = await asMember.PatchAsJsonAsync($"/api/tasks/{task.Id}/status", new { status });
            Assert.Equal(HttpStatusCode.OK, step.StatusCode);
        }
        var done = await ReadAsync<TaskResponse>(await asMember.GetAsync($"/api/tasks/{task.Id}"));
        Assert.Equal(TaskItemStatus.Done, done.Status);
        Assert.NotNull(done.CompletedAtUtc);
        Assert.Equal(HttpStatusCode.Forbidden, (await asMember.PatchAsJsonAsync($"/api/tasks/{task.Id}/status", new { status = "InProgress" })).StatusCode);

        // ---------- Comments ----------
        var comment = await asMember.PostAsJsonAsync($"/api/tasks/{task.Id}/comments", new { content = "Done and tested" });
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
        var comments = await ReadAsync<PagedResult<CommentResponse>>(await asManager.GetAsync($"/api/tasks/{task.Id}/comments"));
        Assert.Single(comments.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await asOutsider.GetAsync($"/api/tasks/{task.Id}/comments")).StatusCode);

        // ---------- Dashboard reflects the member's scope ----------
        var dashboard = await ReadAsync<DashboardResponse>(await asMember.GetAsync("/api/dashboard"));
        Assert.Equal(1, dashboard.ProjectCount);
        Assert.Equal(1, dashboard.Tasks.Total);
        Assert.Equal(1, dashboard.Tasks.CompletedInLast7Days);
        Assert.Equal(0, (await ReadAsync<DashboardResponse>(await asOutsider.GetAsync("/api/dashboard"))).Tasks.Total);

        // ---------- Delete (cascade) ----------
        Assert.Equal(HttpStatusCode.Forbidden, (await asMember.DeleteAsync($"/api/projects/{project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await asManager.DeleteAsync($"/api/projects/{project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await asManager.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
    }

    [Fact]
    public async Task TaskList_IsScopedToVisibleProjects_AndSortedByPriorityRank()
    {
        // Arrange
        var manager = await CreateUserAsync(role: "Manager", prefix: "manager");
        var outsider = await CreateUserAsync(prefix: "outsider");
        var asManager = ClientFor(manager.AccessToken);
        var project = await ReadAsync<ProjectDetailsResponse>(await asManager.PostAsJsonAsync("/api/projects", new { name = "Sorting" }));
        foreach (var priority in new[] { "Low", "Critical", "Medium" })
        {
            (await asManager.PostAsJsonAsync("/api/tasks", new { projectId = project.Id, title = $"{priority} task", priority })).EnsureSuccessStatusCode();
        }

        // Act
        var sorted = await ReadAsync<PagedResult<TaskResponse>>(
            await asManager.GetAsync($"/api/tasks?projectId={project.Id}&sortBy=Priority"));
        var outsiderView = await ReadAsync<PagedResult<TaskResponse>>(
            await ClientFor(outsider.AccessToken).GetAsync($"/api/tasks?projectId={project.Id}"));

        // Assert: business order, not alphabetical (which would be Critical, Low, Medium)
        Assert.Equal([TaskPriority.Critical, TaskPriority.Medium, TaskPriority.Low], sorted.Items.Select(t => t.Priority));
        Assert.Equal(3, sorted.TotalCount);
        Assert.Equal(0, outsiderView.TotalCount);
    }
}
