using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Moq;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.UnitTests.TestHelpers;

/// <summary>
/// UserManager and SignInManager are concrete classes (not interfaces). Their methods are virtual,
/// so Moq can still override them — the constructors just need (mostly null) dependencies.
/// </summary>
public static class MockIdentity
{
    public static Mock<UserManager<ApplicationUser>> UserManager() =>
        new(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

    public static Mock<SignInManager<ApplicationUser>> SignInManager(UserManager<ApplicationUser> userManager) =>
        new(userManager,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null!, null!, null!, null!);
}
