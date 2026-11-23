using System.Net;
using System.Net.Http.Json;
using TaskManagementSystem.IntegrationTests.Infrastructure;
using TaskManagementSystem.Models.DTOs.Categories;

namespace TaskManagementSystem.IntegrationTests;

/// <summary>Role-, policy- and resource-based authorization enforced by the real pipeline.</summary>
public sealed class AuthorizationTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task AdminOnlyEndpoint_UserGets403_AdminGets200()
    {
        var user = await CreateUserAsync();

        var asUser = await ClientFor(user.AccessToken).GetAsync("/api/users");
        var asAdmin = await ClientFor(await AdminTokenAsync()).GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, asUser.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
    }

    [Fact]
    public async Task CategoryCrud_IsAdminOnly_AndFollowsRestSemantics()
    {
        // Arrange
        var user = await CreateUserAsync();
        var admin = ClientFor(await AdminTokenAsync());
        var name = $"Cat-{Guid.NewGuid():N}"[..20];

        // Act
        var userCreate = await ClientFor(user.AccessToken).PostAsJsonAsync("/api/categories", new { name });
        var adminCreate = await admin.PostAsJsonAsync("/api/categories", new { name, description = "From tests" });
        var created = await ReadAsync<CategoryResponse>(adminCreate);
        var duplicate = await admin.PostAsJsonAsync("/api/categories", new { name = name.ToUpperInvariant() });
        var userRead = await ClientFor(user.AccessToken).GetAsync(adminCreate.Headers.Location);
        var delete = await admin.DeleteAsync($"/api/categories/{created.Id}");
        var readDeleted = await admin.GetAsync($"/api/categories/{created.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, userCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, adminCreate.StatusCode);
        Assert.EndsWith($"/api/categories/{created.Id}", adminCreate.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);   // case-insensitive uniqueness
        Assert.Equal(HttpStatusCode.OK, userRead.StatusCode);          // any logged-in user can read
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, readDeleted.StatusCode);
    }

    [Fact]
    public async Task ProjectCreation_RequiresManagerOrAdmin()
    {
        var user = await CreateUserAsync();
        var manager = await CreateUserAsync(role: "Manager");

        var asUser = await ClientFor(user.AccessToken).PostAsJsonAsync("/api/projects", new { name = "Nope" });
        var asManager = await ClientFor(manager.AccessToken).PostAsJsonAsync("/api/projects", new { name = "Allowed" });

        Assert.Equal(HttpStatusCode.Forbidden, asUser.StatusCode);
        Assert.Equal(HttpStatusCode.Created, asManager.StatusCode);
    }

    [Fact]
    public async Task UserProfile_IsResourceBased_SelfOrAdminOnly()
    {
        var alice = await CreateUserAsync(prefix: "alice");
        var bob = await CreateUserAsync(prefix: "bob");

        var own = await ClientFor(alice.AccessToken).GetAsync($"/api/users/{alice.Id}");
        var other = await ClientFor(alice.AccessToken).GetAsync($"/api/users/{bob.Id}");
        var asAdmin = await ClientFor(await AdminTokenAsync()).GetAsync($"/api/users/{bob.Id}");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
    }
}
