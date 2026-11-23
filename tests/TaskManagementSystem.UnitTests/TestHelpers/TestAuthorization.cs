using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using TaskManagementSystem.Authorization.Handlers;

namespace TaskManagementSystem.UnitTests.TestHelpers;

/// <summary>
/// The REAL IAuthorizationService with the application's real resource handlers.
/// Services are tested together with their authorization rules (a "sociable" unit test) instead of
/// mocking IAuthorizationService — mocking it would only test our own mock setup.
/// </summary>
public static class TestAuthorization
{
    public static IAuthorizationService Create()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();
        services.AddSingleton<IAuthorizationHandler, UserAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, ProjectAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, TaskAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, CommentAuthorizationHandler>();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }
}
