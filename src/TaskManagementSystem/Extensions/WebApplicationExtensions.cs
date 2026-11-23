using TaskManagementSystem.Data.Seed;

namespace TaskManagementSystem.Extensions;

/// <summary>Startup tasks that run after the app is built. (API documentation setup: OpenApiExtensions.)</summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Runs the idempotent startup seeder (admin user). The app's root service provider has no
    /// HTTP request scope, so we create a scope manually to resolve scoped services
    /// (DbSeeder → UserManager → DbContext).
    /// </summary>
    public static async Task SeedDatabaseAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
        await seeder.SeedAsync();
    }
}
