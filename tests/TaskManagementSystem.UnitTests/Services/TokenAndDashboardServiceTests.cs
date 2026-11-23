using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using TaskManagementSystem.Configuration;
using TaskManagementSystem.Constants;
using TaskManagementSystem.Enums;
using TaskManagementSystem.Helpers;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Services;
using TaskManagementSystem.UnitTests.TestHelpers;

namespace TaskManagementSystem.UnitTests.Services;

public sealed class TokenServiceTests
{
    private static readonly JwtSettings Settings = new()
    {
        Issuer = "tests-issuer", Audience = "tests-audience", SigningKey = new string('k', 64),
        AccessTokenLifetimeMinutes = 15, RefreshTokenLifetimeDays = 7
    };

    private static TokenService CreateService() => new(Options.Create(Settings), new FakeTimeProvider(TestData.Now));

    [Fact]
    public void CreateAccessToken_ContainsExpectedClaims_AndExpiresAfter15Minutes()
    {
        // Arrange
        var user = TestData.User("Jane");
        user.EmailConfirmed = true;

        // Act
        var token = CreateService().CreateAccessToken(user, [Roles.Manager, Roles.User]);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.Token);

        // Assert
        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal(user.Email, jwt.GetClaim(AppClaimTypes.Email).Value);
        Assert.Equal("true", jwt.GetClaim(AppClaimTypes.EmailVerified).Value);
        Assert.Equal([Roles.Manager, Roles.User], jwt.Claims.Where(c => c.Type == AppClaimTypes.Role).Select(c => c.Value));
        Assert.Equal("tests-issuer", jwt.Issuer);
        Assert.Equal(TestData.Now.UtcDateTime.AddMinutes(15), jwt.ValidTo);
        Assert.Equal(TestData.Now.UtcDateTime.AddMinutes(15), token.ExpiresAtUtc);
    }

    [Fact]
    public async Task CreateAccessToken_SignatureValidatesWithTheKey_AndFailsWithAnotherKey()
    {
        // Arrange
        var token = CreateService().CreateAccessToken(TestData.User("Jane"), [Roles.User]).Token;
        TokenValidationParameters Parameters(string key) => new()
        {
            ValidIssuer = Settings.Issuer,
            ValidAudience = Settings.Audience,
            IssuerSigningKey = new JwtSettings { SigningKey = key }.CreateSigningKey(),
            ValidateLifetime = false // the fake clock is not the real clock; lifetime is asserted above
        };
        var handler = new JsonWebTokenHandler();

        // Act
        var valid = await handler.ValidateTokenAsync(token, Parameters(Settings.SigningKey));
        var forged = await handler.ValidateTokenAsync(token, Parameters(new string('x', 64)));

        // Assert
        Assert.True(valid.IsValid);
        Assert.False(forged.IsValid);
    }

    [Fact]
    public void CreateRefreshToken_StoresOnlyA64CharHash_AndExpiresIn7Days()
    {
        var userId = Guid.NewGuid();

        var issued = CreateService().CreateRefreshToken(userId, "10.0.0.1");

        Assert.Equal(86, issued.RawToken.Length);                       // 64 random bytes, Base64Url
        Assert.Equal(64, issued.Entity.TokenHash.Length);               // SHA-256 hex
        Assert.Equal(SecureTokenGenerator.Hash(issued.RawToken), issued.Entity.TokenHash);
        Assert.DoesNotContain(issued.RawToken, issued.Entity.TokenHash);
        Assert.Equal(TestData.Now.UtcDateTime.AddDays(7), issued.Entity.ExpiresAtUtc);
        Assert.Equal(userId, issued.Entity.UserId);
    }

    [Fact]
    public void CreateRefreshToken_TwoCalls_ProduceDifferentTokens()
    {
        var service = CreateService();

        Assert.NotEqual(service.CreateRefreshToken(Guid.NewGuid(), null).RawToken, service.CreateRefreshToken(Guid.NewGuid(), null).RawToken);
    }
}

public sealed class DashboardServiceTests
{
    private readonly Mock<IDashboardRepository> _repository = new();

    public DashboardServiceTests()
    {
        _repository.Setup(r => r.GetTaskCountersAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new TaskCounters(5, 3, 1, 1, 1, 2, 1, 1));
        _repository.Setup(r => r.CountByStatusAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new Dictionary<TaskItemStatus, int> { [TaskItemStatus.Todo] = 2, [TaskItemStatus.Done] = 1 });
        _repository.Setup(r => r.CountByPriorityAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new Dictionary<TaskPriority, int>());
        _repository.Setup(r => r.GetProjectProgressAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<DateTime>(), 10, It.IsAny<CancellationToken>()))
                   .ReturnsAsync([]);
    }

    [Fact]
    public async Task GetDashboardAsync_FillsMissingStatusesAndPrioritiesWithZero_InEnumOrder()
    {
        var service = new DashboardService(_repository.Object, new FakeTimeProvider(TestData.Now));

        var dashboard = await service.GetDashboardAsync(TestUsers.Create(Guid.NewGuid(), Roles.User), CancellationToken.None);

        Assert.Equal(Enum.GetValues<TaskItemStatus>(), dashboard.TasksByStatus.Keys);
        Assert.Equal(0, dashboard.TasksByStatus[TaskItemStatus.InReview]);
        Assert.Equal(2, dashboard.TasksByStatus[TaskItemStatus.Todo]);
        Assert.All(dashboard.TasksByPriority.Values, count => Assert.Equal(0, count));
        Assert.Equal(4, dashboard.TasksByPriority.Count);
    }

    [Fact]
    public async Task GetDashboardAsync_MapsCounters_AndScopesByRole()
    {
        var service = new DashboardService(_repository.Object, new FakeTimeProvider(TestData.Now));

        var user = await service.GetDashboardAsync(TestUsers.Create(Guid.NewGuid(), Roles.User), CancellationToken.None);
        var admin = await service.GetDashboardAsync(TestUsers.Admin(), CancellationToken.None);

        Assert.Equal("MyProjects", user.Scope);
        Assert.Equal("AllProjects", admin.Scope);
        Assert.Equal(3, user.Tasks.Open);
        Assert.Equal(2, user.MyWork.AssignedOpen);
        Assert.Equal(TestData.Now.UtcDateTime, user.GeneratedAtUtc);
        _repository.Verify(r => r.CountProjectsAsync(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()), Times.Once);
    }
}
