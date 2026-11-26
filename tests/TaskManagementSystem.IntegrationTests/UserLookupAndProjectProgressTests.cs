using System.Net;
using System.Net.Http.Json;
using TaskManagementSystem.IntegrationTests.Infrastructure;
using TaskManagementSystem.Models.DTOs.Common;
using TaskManagementSystem.Models.DTOs.Projects;
using TaskManagementSystem.Models.DTOs.Tasks;
using TaskManagementSystem.Models.DTOs.Users;

namespace TaskManagementSystem.IntegrationTests;

/// <summary>
/// Endpoints added for the React frontend:
///   GET /api/users/lookup               → find users to add as project members (Admin/Manager)
///   GET /api/projects (progress fields) → openTaskCount, doneTaskCount, completionPercent
/// </summary>
public sealed class UserLookupAndProjectProgressTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task UserLookup_IsForVerifiedAdminsAndManagersOnly()
    {
        // Arrange
        var user = await CreateUserAsync();
        var unverifiedManager = await CreateUserAsync(role: "Manager", confirmEmail: false);
        var manager = await CreateUserAsync(role: "Manager");

        // Act
        var asUser = await ClientFor(user.AccessToken).GetAsync("/api/users/lookup?search=test");
        var asUnverifiedManager = await ClientFor(unverifiedManager.AccessToken).GetAsync("/api/users/lookup?search=test");
        var asManager = await ClientFor(manager.AccessToken).GetAsync("/api/users/lookup?search=test");
        var asAdmin = await ClientFor(await AdminTokenAsync()).GetAsync("/api/users/lookup?search=test");
        var anonymous = await AnonymousClient().GetAsync("/api/users/lookup?search=test");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, asUser.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asUnverifiedManager.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asManager.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task UserLookup_FindsActiveUsersByName_ExcludesInactive_AndHonoursLimit()
    {
        // Arrange: three users share a unique last name; one of them is deactivated
        var tag = $"lk{Guid.NewGuid():N}"[..12];
        var alice = await CreateUserAsync(prefix: tag);
        var bob = await CreateUserAsync(prefix: tag);
        var carol = await CreateUserAsync(prefix: tag);
        var manager = await CreateUserAsync(role: "Manager");
        var admin = ClientFor(await AdminTokenAsync());
        (await admin.PatchAsJsonAsync($"/api/users/{carol.Id}/status", new { isActive = false })).EnsureSuccessStatusCode();
        var asManager = ClientFor(manager.AccessToken);

        // Act
        var all = await ReadAsync<List<UserLookupResponse>>(await asManager.GetAsync($"/api/users/lookup?search={tag}"));
        var limited = await ReadAsync<List<UserLookupResponse>>(await asManager.GetAsync($"/api/users/lookup?search={tag}&limit=1"));
        var none = await ReadAsync<List<UserLookupResponse>>(await asManager.GetAsync("/api/users/lookup?search=zz-no-such-user-zz"));

        // Assert
        Assert.Equal(new[] { alice.Id, bob.Id }.Order(), all.Select(u => u.Id).Order());
        Assert.DoesNotContain(all, u => u.Id == carol.Id);
        Assert.All(all, u => Assert.Equal($"Test {tag}", u.FullName));
        Assert.Contains(all, u => u.Email == alice.Email);
        Assert.Single(limited);
        Assert.Empty(none);
    }

    [Theory]
    [InlineData("/api/users/lookup")]
    [InlineData("/api/users/lookup?search=a")]
    [InlineData("/api/users/lookup?search=test&limit=0")]
    [InlineData("/api/users/lookup?search=test&limit=21")]
    public async Task UserLookup_InvalidQuery_Returns400ProblemDetails(string url)
    {
        var response = await ClientFor(await AdminTokenAsync()).GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.True(problem.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task UserLookup_ResultCanBeAddedAsProjectMember()
    {
        // Arrange: the end-to-end reason this endpoint exists
        var tag = $"mb{Guid.NewGuid():N}"[..12];
        var candidate = await CreateUserAsync(prefix: tag);
        var manager = await CreateUserAsync(role: "Manager");
        var asManager = ClientFor(manager.AccessToken);
        var project = await ReadAsync<ProjectDetailsResponse>(await asManager.PostAsJsonAsync("/api/projects", new { name = $"Team {tag}" }));

        // Act
        var found = (await ReadAsync<List<UserLookupResponse>>(await asManager.GetAsync($"/api/users/lookup?search={tag}"))).Single();
        var add = await asManager.PostAsJsonAsync($"/api/projects/{project.Id}/members", new { userId = found.Id });

        // Assert
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var updated = await ReadAsync<ProjectDetailsResponse>(add);
        Assert.Contains(updated.Members, m => m.UserId == candidate.Id);
    }

    [Fact]
    public async Task ProjectList_ReturnsTaskProgress_SameFormulaAsDashboard()
    {
        // Arrange: 4 tasks → 1 Done, 1 Cancelled, 2 open → 1 / (4 − 1) = 33.3 %
        var manager = await CreateUserAsync(role: "Manager");
        var asManager = ClientFor(manager.AccessToken);
        var name = $"Progress {Guid.NewGuid():N}"[..20];
        var project = await ReadAsync<ProjectDetailsResponse>(await asManager.PostAsJsonAsync("/api/projects", new { name }));
        var taskIds = new List<int>();
        for (var i = 1; i <= 4; i++)
        {
            var task = await ReadAsync<TaskResponse>(await asManager.PostAsJsonAsync("/api/tasks", new { projectId = project.Id, title = $"Task {i}" }));
            taskIds.Add(task.Id);
        }

        foreach (var status in new[] { "InProgress", "InReview", "Done" })
        {
            (await asManager.PatchAsJsonAsync($"/api/tasks/{taskIds[0]}/status", new { status })).EnsureSuccessStatusCode();
        }

        (await asManager.PatchAsJsonAsync($"/api/tasks/{taskIds[1]}/status", new { status = "Cancelled" })).EnsureSuccessStatusCode();

        // Act
        var page = await ReadAsync<PagedResult<ProjectSummaryResponse>>(
            await asManager.GetAsync($"/api/projects?search={Uri.EscapeDataString(name)}"));
        var emptyProject = await ReadAsync<ProjectDetailsResponse>(await asManager.PostAsJsonAsync("/api/projects", new { name = name + "-empty" }));
        var emptyPage = await ReadAsync<PagedResult<ProjectSummaryResponse>>(
            await asManager.GetAsync($"/api/projects?search={Uri.EscapeDataString(name + "-empty")}"));

        // Assert
        var summary = Assert.Single(page.Items, p => p.Id == project.Id);
        Assert.Equal(4, summary.TaskCount);
        Assert.Equal(2, summary.OpenTaskCount);
        Assert.Equal(1, summary.DoneTaskCount);
        Assert.Equal(33.3, summary.CompletionPercent);

        var empty = Assert.Single(emptyPage.Items, p => p.Id == emptyProject.Id);
        Assert.Equal(0, empty.TaskCount);
        Assert.Equal(0, empty.CompletionPercent);
    }
}
