using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskManagementSystem.Data;
using TaskManagementSystem.IntegrationTests.Infrastructure;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Models.DTOs.Categories;

namespace TaskManagementSystem.IntegrationTests;

/// <summary>Validation responses, exception responses (incl. 500), and test-infrastructure guarantees.</summary>
public sealed class ErrorAndInfrastructureTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public void Application_UsesTheIntegrationTestDatabase_NotTheDevelopmentDatabase()
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(CustomWebApplicationFactory.DatabaseName, dbContext.Database.GetDbConnection().Database);
    }

    [Fact]
    public async Task Health_IsAnonymous_AndEveryResponseHasACorrelationId()
    {
        var response = await AnonymousClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.Matches("^[0-9a-f]{32}$", values!.Single());
    }

    [Fact]
    public async Task ValidationError_IsProblemDetails_WithFieldErrors_CorrelationAndTraceIds()
    {
        var response = await AnonymousClient().PostAsJsonAsync("/api/auth/register",
            new { email = "not-an-email", password = "weak", firstName = "", lastName = "Doe" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.True(errors.TryGetProperty("password", out var passwordErrors));
        Assert.True(passwordErrors.GetArrayLength() >= 3);
        Assert.True(errors.TryGetProperty("firstName", out _));
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("correlationId").GetString()));
        Assert.StartsWith("00-", problem.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task ModelBindingErrors_UseCamelCaseKeys_AndHideInternalTypeNames()
    {
        var user = await CreateUserAsync();
        var client = ClientFor(user.AccessToken);

        var badEnum = await client.PostAsJsonAsync("/api/tasks", new { projectId = 1, title = "x", priority = "Urgent" });
        var badQuery = await client.GetAsync("/api/tasks?status=Started");

        var body = await badEnum.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, badEnum.StatusCode);
        Assert.Contains("\"priority\"", body);
        Assert.DoesNotContain("TaskManagementSystem.", body);
        Assert.True((await ReadProblemAsync(badQuery)).GetProperty("errors").TryGetProperty("status", out _));
    }

    [Fact]
    public async Task NotFound_UnknownResourceAndUnknownRoute_AreProblemDetails()
    {
        var client = ClientFor((await CreateUserAsync()).AccessToken);

        var unknownCategory = await client.GetAsync("/api/categories/999999");
        var unknownRoute = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, unknownCategory.StatusCode);
        Assert.Equal("Category with id '999999' was not found.", (await ReadProblemAsync(unknownCategory)).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.NotFound, unknownRoute.StatusCode);
        Assert.True((await ReadProblemAsync(unknownRoute)).TryGetProperty("correlationId", out _));
    }

    [Fact]
    public async Task UnhandledException_Returns500_WithoutLeakingDetails()
    {
        // Arrange: a variant of the app whose category service crashes with a "sensitive" message.
        await using var crashingApp = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICategoryService>();
            services.AddScoped<ICategoryService, CrashingCategoryService>();
        }));
        var client = crashingApp.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", await AdminTokenAsync());

        // Act
        var response = await client.GetAsync("/api/categories");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("An unexpected error occurred. Please try again later.", body);
        Assert.DoesNotContain("secret-db", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.True((await ReadProblemAsync(response)).TryGetProperty("correlationId", out _));
    }

    private sealed class CrashingCategoryService : ICategoryService
    {
        private static Exception Boom() => new InvalidOperationException("Connection to Server=secret-db;Password=hunter2 failed");

        public Task<IReadOnlyList<CategoryResponse>> GetAllAsync(CancellationToken cancellationToken) => throw Boom();
        public Task<CategoryResponse> GetByIdAsync(int id, CancellationToken cancellationToken) => throw Boom();
        public Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken) => throw Boom();
        public Task<CategoryResponse> UpdateAsync(int id, UpdateCategoryRequest request, CancellationToken cancellationToken) => throw Boom();
        public Task DeleteAsync(int id, CancellationToken cancellationToken) => throw Boom();
    }
}
