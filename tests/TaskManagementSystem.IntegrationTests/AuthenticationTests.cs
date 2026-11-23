using System.Net;
using System.Net.Http.Json;
using TaskManagementSystem.IntegrationTests.Infrastructure;
using TaskManagementSystem.Models.DTOs.Auth;
using TaskManagementSystem.Models.DTOs.Users;

namespace TaskManagementSystem.IntegrationTests;

/// <summary>Registration, login, JWT-protected endpoints, refresh rotation and logout — over real HTTP.</summary>
public sealed class AuthenticationTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Register_ReturnsCreated_AndSendsConfirmationEmail()
    {
        // Arrange
        var email = NewEmail();

        // Act
        var response = await AnonymousClient().PostAsJsonAsync("/api/auth/register",
            new { email, password = Password, firstName = "Jane", lastName = "Doe" });

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var profile = await ReadAsync<UserProfileResponse>(response);
        Assert.Equal(email, profile.Email);
        Assert.Equal(["User"], profile.Roles);
        Assert.False(profile.EmailConfirmed);
        Assert.Equal(profile.Id, Factory.Emails.GetEmailConfirmation(email).UserId);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var user = await CreateUserAsync();

        var response = await AnonymousClient().PostAsJsonAsync("/api/auth/register",
            new { email = user.Email, password = Password, firstName = "Jane", lastName = "Doe" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("An account with this email address already exists.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401_WithGenericMessage()
    {
        var user = await CreateUserAsync();

        var response = await AnonymousClient().PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "Wrong!Pass1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Invalid email or password.", (await ReadProblemAsync(response)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ProtectedEndpoint_RequiresAValidToken()
    {
        // Arrange
        var user = await CreateUserAsync();
        var tampered = user.AccessToken[..^4] + (user.AccessToken.EndsWith("AAAA") ? "BBBB" : "AAAA");

        // Act
        var withoutToken = await AnonymousClient().GetAsync("/api/auth/me");
        var withToken = await ClientFor(user.AccessToken).GetAsync("/api/auth/me");
        var withTamperedToken = await ClientFor(tampered).GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, withoutToken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withToken.StatusCode);
        Assert.Equal(user.Email, (await ReadAsync<UserProfileResponse>(withToken)).Email);
        Assert.Equal(HttpStatusCode.Unauthorized, withTamperedToken.StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesTokens_AndReusingAnOldTokenRevokesAllSessions()
    {
        // Arrange
        var user = await CreateUserAsync();
        var client = AnonymousClient();

        // Act 1: normal rotation
        var first = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = user.RefreshToken });
        var rotated = await ReadAsync<AuthResponse>(first);

        // Act 2: the OLD token is presented again (theft scenario)
        var reuse = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = user.RefreshToken });

        // Act 3: the legitimate NEW token was revoked as a consequence
        var afterReuse = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = rotated.RefreshToken });

        // Assert
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.NotEqual(user.RefreshToken, rotated.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesTheRefreshToken()
    {
        var user = await CreateUserAsync();
        var client = AnonymousClient();

        var logout = await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = user.RefreshToken });
        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = user.RefreshToken });

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task EmailVerification_UnlocksWriteEndpoints()
    {
        // Arrange: unverified user
        var user = await CreateUserAsync(confirmEmail: false);
        var update = new { firstName = "New", lastName = "Name", jobTitle = "Dev" };

        // Act
        var beforeConfirm = await ClientFor(user.AccessToken).PutAsJsonAsync("/api/users/me", update);
        var (userId, token) = Factory.Emails.GetEmailConfirmation(user.Email);
        var confirm = await AnonymousClient().PostAsJsonAsync("/api/account/confirm-email", new { userId, token });
        var newToken = (await LoginAsync(user.Email, user.Password)).AccessToken; // claim email_verified = true
        var afterConfirm = await ClientFor(newToken).PutAsJsonAsync("/api/users/me", update);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, beforeConfirm.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterConfirm.StatusCode);
    }

    [Fact]
    public async Task PasswordReset_EndToEnd()
    {
        // Arrange
        var user = await CreateUserAsync();
        var client = AnonymousClient();
        const string newPassword = "N3w!Passw0rd";

        // Act
        var forgot = await client.PostAsJsonAsync("/api/account/forgot-password", new { email = user.Email });
        var token = Factory.Emails.GetPasswordResetToken(user.Email);
        var reset = await client.PostAsJsonAsync("/api/account/reset-password", new { email = user.Email, token, newPassword });
        var oldLogin = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        var newLogin = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = newPassword });

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }
}
