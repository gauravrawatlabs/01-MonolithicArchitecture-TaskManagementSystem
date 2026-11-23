using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskManagementSystem.Data;
using TaskManagementSystem.Interfaces.Services;

namespace TaskManagementSystem.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the REAL application in memory (TestServer): the same Program.cs, middleware pipeline,
/// DI registrations, EF Core and SQL Server — only configuration and the email sender differ.
///
/// - Database: a SEPARATE SQL Server database (TaskManagementDb_IntegrationTests), dropped and
///   re-created from the migrations once per test run. The development database is never touched.
/// - Secrets: test-only values via UseSetting (no User Secrets in the "Testing" environment).
/// - Email: an in-memory CapturingEmailSender so tests can read confirmation/reset tokens.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DatabaseName = "TaskManagementDb_IntegrationTests";
    public const string ConnectionString =
        $"Server=localhost;Database={DatabaseName};Trusted_Connection=True;TrustServerCertificate=True";
    public const string AdminEmail = "admin@integration.test";
    public const string AdminPassword = "Adm1n!Integration";

    public CapturingEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting values are visible to Program.cs immediately (before builder.Build()),
        // which matters because AddDatabase / AddCorsPolicy read configuration eagerly.
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-0123456789-abcdefghijklmnopqrstuvwxyz");
        builder.UseSetting("SeedAdmin:Email", AdminEmail);
        builder.UseSetting("SeedAdmin:Password", AdminPassword);
        builder.UseSetting("ApiDocumentation:Enabled", "true");   // so the OpenAPI contract can be tested

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    /// <summary>Runs once before the first test: fresh database from the migrations.</summary>
    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options;
        await using var dbContext = new ApplicationDbContext(options);

        // Safety guard: refuse to drop anything that isn't the integration-test database.
        EnsureIsTestDatabase(dbContext.Database.GetDbConnection());

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();      // also proves the migrations build a working schema
    }

    // The database is kept after the run so it can be inspected; the next run recreates it.
    Task IAsyncLifetime.DisposeAsync() => base.DisposeAsync().AsTask();

    public static void EnsureIsTestDatabase(DbConnection connection)
    {
        if (connection.Database != DatabaseName)
        {
            throw new InvalidOperationException(
                $"Integration tests must run against '{DatabaseName}', not '{connection.Database}'.");
        }
    }
}

[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<CustomWebApplicationFactory>
{
    // All integration test classes share ONE application instance and ONE database (fast),
    // and run sequentially. Tests stay independent by creating their own users/projects (unique emails).
    public const string Name = "Integration";
}
