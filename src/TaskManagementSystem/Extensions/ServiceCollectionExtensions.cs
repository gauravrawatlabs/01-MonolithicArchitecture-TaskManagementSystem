using Microsoft.EntityFrameworkCore;
using TaskManagementSystem.Data;

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
}
