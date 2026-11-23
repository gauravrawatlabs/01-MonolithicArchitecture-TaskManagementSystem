using Microsoft.EntityFrameworkCore;
using TaskManagementSystem.Data;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Repositories;
using TaskManagementSystem.Services;

namespace TaskManagementSystem.Extensions;

/// <summary>
/// Groups service registrations by concern so Program.cs stays a readable composition root.
/// </summary>
public static class ServiceCollectionExtensions
{
    private const string ConnectionStringName = "DefaultConnection";

    public static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Fail fast at startup instead of on the first request.
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. " +
                "Set it in appsettings.Development.json (local) or the " +
                $"'ConnectionStrings__{ConnectionStringName}' environment variable.");
        }

        // AddDbContext registers ApplicationDbContext with a SCOPED lifetime (one per HTTP request).
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlServer(connectionString);

            if (environment.IsDevelopment())
            {
                // Richer exception messages while developing. We deliberately do NOT call
                // EnableSensitiveDataLogging(): it would write parameter values
                // (password hashes, token hashes, emails) into the logs.
                options.EnableDetailedErrors();
            }
        });

        return services;
    }

    /// <summary>
    /// Registers repositories and services (interface → implementation).
    ///
    /// Lifetimes:
    /// - Repositories: SCOPED — they depend on the scoped DbContext. A singleton repository would
    ///   capture one DbContext forever (captive dependency) and share it across requests.
    /// - Services: SCOPED — they depend on scoped repositories; one instance per request is cheap
    ///   and keeps all work of a request in the same DbContext / Unit of Work.
    /// - TimeProvider: SINGLETON — stateless and thread-safe; one shared instance is enough.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ICategoryService, CategoryService>();

        return services;
    }

    /// <summary>Generates the OpenAPI document (served at /openapi/v1.json).</summary>
    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Task Management System API";
                document.Info.Version = "v1";
                document.Info.Description =
                    "ASP.NET Core (.NET 10) monolithic Web API for managing projects, tasks, categories and comments.";
                return Task.CompletedTask;
            });
        });

        return services;
    }
}
