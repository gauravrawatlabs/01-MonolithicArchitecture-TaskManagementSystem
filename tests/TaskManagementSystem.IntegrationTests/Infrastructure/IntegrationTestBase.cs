using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskManagementSystem.Models.DTOs.Auth;
using TaskManagementSystem.Models.DTOs.Users;

namespace TaskManagementSystem.IntegrationTests.Infrastructure;

public sealed record TestUser(Guid Id, string Email, string Password, string AccessToken, string RefreshToken);

/// <summary>
/// Shared helpers: HTTP clients, creating users through the PUBLIC API (register → confirm email →
/// optional role → login), and reading ProblemDetails. Tests talk to the app only over HTTP.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public abstract class IntegrationTestBase(CustomWebApplicationFactory factory)
{
    public const string Password = "Str0ng!Pass";

    /// <summary>Same JSON settings as the API (camelCase, enums as strings).</summary>
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    protected CustomWebApplicationFactory Factory { get; } = factory;

    protected HttpClient AnonymousClient() => Factory.CreateClient();

    protected HttpClient ClientFor(string accessToken)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    protected static string NewEmail(string prefix = "user") => $"{prefix}-{Guid.NewGuid():N}@integration.test";

    protected async Task<AuthResponse> LoginAsync(string email, string password)
    {
        var response = await AnonymousClient().PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
    }

    protected async Task<string> AdminTokenAsync() =>
        (await LoginAsync(CustomWebApplicationFactory.AdminEmail, CustomWebApplicationFactory.AdminPassword)).AccessToken;

    /// <summary>Registers a user via the API, optionally confirms the email and assigns a role, then logs in.</summary>
    protected async Task<TestUser> CreateUserAsync(string? role = null, bool confirmEmail = true, string prefix = "user")
    {
        var email = NewEmail(prefix);
        var client = AnonymousClient();

        var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password, firstName = "Test", lastName = prefix });
        register.EnsureSuccessStatusCode();
        var profile = (await register.Content.ReadFromJsonAsync<UserProfileResponse>(Json))!;

        if (confirmEmail)
        {
            var (userId, token) = Factory.Emails.GetEmailConfirmation(email);
            (await client.PostAsJsonAsync("/api/account/confirm-email", new { userId, token })).EnsureSuccessStatusCode();
        }

        if (role is not null)
        {
            var admin = ClientFor(await AdminTokenAsync());
            (await admin.PutAsJsonAsync($"/api/users/{profile.Id}/roles", new { roles = new[] { role } })).EnsureSuccessStatusCode();
        }

        var auth = await LoginAsync(email, Password); // after role change → token carries the new role
        return new TestUser(profile.Id, email, Password, auth.AccessToken, auth.RefreshToken);
    }

    /// <summary>Asserts the RFC 9457 content type and returns the parsed body.</summary>
    protected static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;
}
