using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using TaskManagementSystem.Configuration;
using TaskManagementSystem.Exceptions;
using TaskManagementSystem.Helpers;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Models.DTOs.Auth;
using TaskManagementSystem.Models.Entities;
using TaskManagementSystem.Services;
using TaskManagementSystem.UnitTests.TestHelpers;

namespace TaskManagementSystem.UnitTests.Services;

/// <summary>
/// AuthService login / refresh / logout logic with mocked Identity and repository.
/// The token service is REAL (it has no I/O), so tokens are actually generated and hashed.
/// Rotation's transaction path needs a real database → covered by the integration tests.
/// </summary>
public sealed class AuthServiceTests
{
    private const string GenericLoginError = "Invalid email or password.";

    private readonly Mock<UserManager<ApplicationUser>> _userManager = MockIdentity.UserManager();
    private readonly Mock<SignInManager<ApplicationUser>> _signInManager;
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly FakeTimeProvider _clock = new(TestData.Now);
    private readonly TokenService _tokenService;
    private readonly ApplicationUser _user = TestData.User("Jane");

    public AuthServiceTests()
    {
        _signInManager = MockIdentity.SignInManager(_userManager.Object);
        _tokenService = new TokenService(Options.Create(new JwtSettings
        {
            Issuer = "tests", Audience = "tests", SigningKey = new string('k', 64),
            AccessTokenLifetimeMinutes = 15, RefreshTokenLifetimeDays = 7
        }), _clock);

        _userManager.Setup(m => m.FindByEmailAsync(_user.Email!)).ReturnsAsync(_user);
        _userManager.Setup(m => m.GetRolesAsync(_user)).ReturnsAsync(["User"]);
        _userManager.Setup(m => m.UpdateAsync(_user)).ReturnsAsync(IdentityResult.Success);
    }

    // ApplicationDbContext is only used by RegisterAsync / successful rotation (transactions).
    private AuthService CreateService() => new(
        _userManager.Object, _signInManager.Object, null!, _refreshTokens.Object, _tokenService,
        Mock.Of<IAccountEmailService>(), _clock, NullLogger<AuthService>.Instance);

    private void GivenPasswordCheckReturns(SignInResult result) =>
        _signInManager.Setup(s => s.CheckPasswordSignInAsync(_user, It.IsAny<string>(), true)).ReturnsAsync(result);

    // ---------- Login ----------

    [Fact]
    public async Task LoginAsync_UnknownEmail_ThrowsGenericUnauthorized_WithoutCheckingPassword()
    {
        var exception = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            CreateService().LoginAsync(new LoginRequest("nobody@example.com", "x"), null, CancellationToken.None));

        Assert.Equal(GenericLoginError, exception.Message);
        _signInManager.Verify(s => s.CheckPasswordSignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_InactiveUser_ThrowsGenericUnauthorized()
    {
        _user.IsActive = false;

        var exception = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            CreateService().LoginAsync(new LoginRequest(_user.Email!, "Str0ng!Pass"), null, CancellationToken.None));

        Assert.Equal(GenericLoginError, exception.Message);
    }

    [Fact]
    public async Task LoginAsync_LockedOut_ThrowsTheSameGenericMessage()
    {
        GivenPasswordCheckReturns(SignInResult.LockedOut);

        var exception = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            CreateService().LoginAsync(new LoginRequest(_user.Email!, "Str0ng!Pass"), null, CancellationToken.None));

        Assert.Equal(GenericLoginError, exception.Message); // no "account locked" hint for attackers
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsGenericUnauthorized_AndIssuesNoToken()
    {
        GivenPasswordCheckReturns(SignInResult.Failed);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            CreateService().LoginAsync(new LoginRequest(_user.Email!, "Wrong!Pass1"), null, CancellationToken.None));

        _refreshTokens.Verify(r => r.Add(It.IsAny<RefreshToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_Success_ReturnsTokens_StoresOnlyTheRefreshTokenHash()
    {
        // Arrange
        GivenPasswordCheckReturns(SignInResult.Success);
        RefreshToken? stored = null;
        _refreshTokens.Setup(r => r.Add(It.IsAny<RefreshToken>())).Callback<RefreshToken>(t => stored = t);

        // Act
        var response = await CreateService().LoginAsync(new LoginRequest(_user.Email!, "Str0ng!Pass"), "10.0.0.1", CancellationToken.None);

        // Assert
        Assert.False(string.IsNullOrEmpty(response.AccessToken));
        Assert.Equal(TestData.Now.UtcDateTime.AddMinutes(15), response.AccessTokenExpiresAtUtc);
        Assert.NotNull(stored);
        Assert.NotEqual(response.RefreshToken, stored.TokenHash);                        // raw token NOT stored
        Assert.Equal(SecureTokenGenerator.Hash(response.RefreshToken), stored.TokenHash); // only its hash
        Assert.Equal("10.0.0.1", stored.CreatedByIp);
        Assert.Equal(TestData.Now.UtcDateTime, _user.LastLoginAtUtc);
        _refreshTokens.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------- Refresh ----------

    private RefreshToken GivenStoredToken(string raw, Action<RefreshToken>? configure = null)
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = _user.Id,
            User = _user,
            TokenHash = SecureTokenGenerator.Hash(raw),
            CreatedAtUtc = TestData.Now.UtcDateTime.AddDays(-1),
            ExpiresAtUtc = TestData.Now.UtcDateTime.AddDays(6)
        };
        configure?.Invoke(token);
        _refreshTokens.Setup(r => r.GetByHashAsync(token.TokenHash, It.IsAny<CancellationToken>())).ReturnsAsync(token);
        return token;
    }

    [Fact]
    public async Task RefreshAsync_UnknownToken_ThrowsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            CreateService().RefreshAsync("unknown", null, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshAsync_ReuseOfRotatedToken_RevokesAllSessionsOfTheUser()
    {
        // Arrange: the token was already rotated (revoked AND replaced by a successor).
        GivenStoredToken("stolen", t => { t.RevokedAtUtc = TestData.Now.UtcDateTime.AddHours(-1); t.ReplacedByTokenId = Guid.NewGuid(); });

        // Act
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            CreateService().RefreshAsync("stolen", "6.6.6.6", CancellationToken.None));

        // Assert
        _refreshTokens.Verify(r => r.RevokeAllActiveForUserAsync(
            _user.Id, TestData.Now.UtcDateTime, "6.6.6.6", It.Is<string>(reason => reason.Contains("Reuse")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_TokenRevokedByLogout_DoesNotRevokeOtherSessions()
    {
        GivenStoredToken("logged-out", t => { t.RevokedAtUtc = TestData.Now.UtcDateTime.AddHours(-1); t.RevokedReason = "Logged out"; });

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            CreateService().RefreshAsync("logged-out", null, CancellationToken.None));

        _refreshTokens.Verify(r => r.RevokeAllActiveForUserAsync(
            It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_ThrowsUnauthorized()
    {
        GivenStoredToken("old", t => t.ExpiresAtUtc = TestData.Now.UtcDateTime.AddSeconds(-1));

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            CreateService().RefreshAsync("old", null, CancellationToken.None));
    }

    // ---------- Logout ----------

    [Fact]
    public async Task LogoutAsync_UnknownToken_DoesNothing()
    {
        await CreateService().LogoutAsync("unknown", null, CancellationToken.None);

        _refreshTokens.Verify(r => r.TryRevokeAsync(
            It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LogoutAsync_ActiveToken_RevokesIt()
    {
        var token = GivenStoredToken("active");

        await CreateService().LogoutAsync("active", "10.0.0.1", CancellationToken.None);

        _refreshTokens.Verify(r => r.TryRevokeAsync(
            token.Id, TestData.Now.UtcDateTime, "10.0.0.1", "Logged out", null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
