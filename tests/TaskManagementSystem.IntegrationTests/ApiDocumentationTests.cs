using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using TaskManagementSystem.IntegrationTests.Infrastructure;

namespace TaskManagementSystem.IntegrationTests;

/// <summary>
/// CONTRACT tests for the generated OpenAPI document. They fail when an endpoint is added/removed
/// without the list being updated, or when documentation rules are broken (missing summary,
/// missing 401 on a secured endpoint, a $ref that doesn't resolve, an example that no longer works...).
/// </summary>
public sealed class ApiDocumentationTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static readonly string[] HttpMethods = ["get", "post", "put", "patch", "delete"];

    /// <summary>The complete public API surface (41 operations). Changing the API means changing this list — deliberately.</summary>
    private static readonly string[] ExpectedOperations =
    [
        "POST /api/account/change-password", "POST /api/account/confirm-email", "POST /api/account/forgot-password",
        "POST /api/account/resend-confirmation", "POST /api/account/reset-password",
        "GET /api/auth/me", "POST /api/auth/login", "POST /api/auth/logout", "POST /api/auth/logout-all",
        "POST /api/auth/refresh", "POST /api/auth/register",
        "GET /api/categories", "POST /api/categories", "GET /api/categories/{id}", "PUT /api/categories/{id}", "DELETE /api/categories/{id}",
        "PUT /api/comments/{id}", "DELETE /api/comments/{id}",
        "GET /api/dashboard",
        "GET /api/projects", "POST /api/projects", "GET /api/projects/{id}", "PUT /api/projects/{id}", "DELETE /api/projects/{id}",
        "POST /api/projects/{id}/members", "DELETE /api/projects/{id}/members/{userId}",
        "GET /api/tasks", "POST /api/tasks", "GET /api/tasks/{id}", "PUT /api/tasks/{id}", "DELETE /api/tasks/{id}",
        "PATCH /api/tasks/{id}/assignee", "PATCH /api/tasks/{id}/status",
        "GET /api/tasks/{taskId}/comments", "POST /api/tasks/{taskId}/comments",
        "GET /api/users", "GET /api/users/lookup", "GET /api/users/{id}", "PUT /api/users/me", "PUT /api/users/{id}/roles", "PATCH /api/users/{id}/status"
    ];

    private static readonly string[] AnonymousOperations =
    [
        "POST /api/auth/register", "POST /api/auth/login", "POST /api/auth/refresh", "POST /api/auth/logout",
        "POST /api/account/confirm-email", "POST /api/account/resend-confirmation",
        "POST /api/account/forgot-password", "POST /api/account/reset-password"
    ];

    private async Task<JsonElement> GetDocumentAsync()
    {
        var response = await AnonymousClient().GetAsync("/openapi/v1.json"); // anonymous on purpose
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static IEnumerable<(string Key, JsonElement Operation)> Operations(JsonElement document) =>
        document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(method => HttpMethods.Contains(method.Name))
                .Select(method => ($"{method.Name.ToUpperInvariant()} {path.Name}", method.Value)));

    [Fact]
    public async Task Document_ListsExactlyTheExpectedEndpoints()
    {
        var actual = Operations(await GetDocumentAsync()).Select(o => o.Key).Order().ToList();

        Assert.Equal(ExpectedOperations.Order().ToList(), actual);
    }

    [Fact]
    public async Task EveryOperation_HasSummary_UniqueOperationId_AndADescribedTag()
    {
        var document = await GetDocumentAsync();
        var describedTags = document.GetProperty("tags").EnumerateArray()
            .Where(t => !string.IsNullOrWhiteSpace(t.GetProperty("description").GetString()))
            .Select(t => t.GetProperty("name").GetString()).ToHashSet();
        var operations = Operations(document).ToList();

        Assert.All(operations, o =>
        {
            Assert.False(string.IsNullOrWhiteSpace(o.Operation.GetProperty("summary").GetString()), $"{o.Key} has no summary");
            Assert.Contains(o.Operation.GetProperty("tags")[0].GetString(), describedTags);
        });
        var operationIds = operations.Select(o => o.Operation.GetProperty("operationId").GetString()).ToList();
        Assert.Equal(operationIds.Count, operationIds.Distinct().Count());
        Assert.Contains("Tasks_ChangeStatus", operationIds);
    }

    [Fact]
    public async Task SecuredOperations_RequireBearer_AndDocument401_AnonymousOnesDoNot()
    {
        var document = await GetDocumentAsync();

        foreach (var (key, operation) in Operations(document))
        {
            var isSecured = operation.TryGetProperty("security", out var security) && security.GetArrayLength() > 0;
            if (AnonymousOperations.Contains(key))
            {
                Assert.False(isSecured, $"{key} should be anonymous");
            }
            else
            {
                Assert.True(isSecured, $"{key} should require a Bearer token");
                Assert.True(security[0].TryGetProperty("Bearer", out _), $"{key} should use the Bearer scheme");
                Assert.True(operation.GetProperty("responses").TryGetProperty("401", out _), $"{key} should document 401");
            }
        }
    }

    [Fact]
    public async Task EveryOperation_DocumentsSuccessAnd500_AndAllErrorsAreProblemJson()
    {
        var document = await GetDocumentAsync();

        foreach (var (key, operation) in Operations(document))
        {
            var responses = operation.GetProperty("responses").EnumerateObject().ToList();
            Assert.Contains(responses, r => r.Name.StartsWith('2'));
            Assert.Contains(responses, r => r.Name == "500");

            foreach (var error in responses.Where(r => r.Name[0] is '4' or '5'))
            {
                Assert.True(error.Value.GetProperty("content").TryGetProperty("application/problem+json", out _),
                    $"{key} {error.Name} should be application/problem+json");
            }
        }
    }

    [Fact]
    public async Task EveryReference_ResolvesToAComponent()
    {
        var document = await GetDocumentAsync();
        var json = document.GetRawText();
        var references = System.Text.RegularExpressions.Regex.Matches(json, "\"\\$ref\":\\s*\"#/components/(\\w+)/([^\"]+)\"");

        Assert.NotEmpty(references);
        foreach (System.Text.RegularExpressions.Match reference in references)
        {
            var section = document.GetProperty("components").GetProperty(reference.Groups[1].Value);
            Assert.True(section.TryGetProperty(reference.Groups[2].Value, out _), $"Unresolved $ref {reference.Value}");
        }
    }

    [Fact]
    public async Task Schemas_UseStringEnums_AndRequestExamplesAreProvided()
    {
        var document = await GetDocumentAsync();

        var statuses = document.GetProperty("components").GetProperty("schemas").GetProperty("TaskItemStatus")
            .GetProperty("enum").EnumerateArray().Select(v => v.GetString());
        Assert.Equal(["Todo", "InProgress", "InReview", "Done", "Cancelled"], statuses);

        var createTask = Operations(document).Single(o => o.Key == "POST /api/tasks").Operation;
        var example = createTask.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("example");
        Assert.Equal("High", example.GetProperty("priority").GetString());
    }

    [Fact]
    public async Task Schemas_DescribeNumbersAsNumbers_NotAsStrings()
    {
        // With the web defaults (AllowReadingFromString) every number was documented as
        // type ["integer", "string"]; generated clients then typed ids as number | string.
        var schemas = (await GetDocumentAsync()).GetProperty("components").GetProperty("schemas");
        var offenders = new List<string>();

        foreach (var schema in schemas.EnumerateObject())
        {
            if (!schema.Value.TryGetProperty("properties", out var properties))
            {
                continue;
            }

            foreach (var property in properties.EnumerateObject())
            {
                if (property.Value.TryGetProperty("type", out var type) &&
                    type.ValueKind == JsonValueKind.Array &&
                    type.EnumerateArray().Any(t => t.GetString() is "integer" or "number") &&
                    type.EnumerateArray().Any(t => t.GetString() == "string"))
                {
                    offenders.Add($"{schema.Name}.{property.Name}");
                }
            }
        }

        Assert.Empty(offenders);
        var taskId = schemas.GetProperty("TaskResponse").GetProperty("properties").GetProperty("id");
        Assert.Equal("integer", taskId.GetProperty("type").GetString());
    }

    [Fact]
    public async Task RequestExamples_ActuallyWork_AgainstTheApi()
    {
        // The documented register + login examples must be accepted by the real validators.
        var document = await GetDocumentAsync();
        JsonElement ExampleFor(string key) => Operations(document).Single(o => o.Key == key).Operation
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("example");
        var client = AnonymousClient();

        var register = await client.PostAsJsonAsync("/api/auth/register", ExampleFor("POST /api/auth/register"));
        var login = await client.PostAsJsonAsync("/api/auth/login", ExampleFor("POST /api/auth/login"));

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task SwaggerUi_IsServed_WhenDocumentationIsEnabled()
    {
        var response = await AnonymousClient().GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("swagger-ui", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Documentation_IsNotExposed_WhenDisabled()
    {
        await using var app = Factory.WithWebHostBuilder(b => b.UseSetting("ApiDocumentation:Enabled", "false"));
        var client = app.CreateClient();

        Assert.NotEqual(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await client.GetAsync("/swagger/index.html")).StatusCode);
    }
}
